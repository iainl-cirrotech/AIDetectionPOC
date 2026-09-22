using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiDetection.Web;

public sealed record AnalysisRequest(byte[] Data, string Filename, string? Subject, string? Sender, string? ReceivedAtUtc, string? CorrelationId);

public sealed class ImageRejectedException(string message) : Exception(message);

public sealed class AnalysisService(
    AppSettings settings,
    IImageClassifier classifier,
    C2paVerifier c2paVerifier,
    IResultStore store,
    ILogger<AnalysisService> logger)
{
    public async Task<AnalysisResult> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        if (request.Data.Length == 0) throw new ImageRejectedException("empty request body");
        if (request.Data.LongLength > settings.MaxImageBytes) throw new ImageRejectedException("image exceeds the configured size limit");

        Image<Rgb24> image;
        try
        {
            var info = Image.Identify(request.Data) ?? throw new ImageRejectedException("not a decodable image");
            if ((long)info.Width * info.Height > settings.MaxImagePixels)
                throw new ImageRejectedException("image exceeds the configured pixel budget");
            image = Image.Load<Rgb24>(request.Data);
        }
        catch (ImageRejectedException) { throw; }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new ImageRejectedException($"not a decodable image: {exception.Message}");
        }

        using (image)
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(request.Data));
            var metadata = MetadataAnalyzer.Analyze(image, request.Data);
            var c2pa = await c2paVerifier.VerifyAsync(request.Data, request.Filename, cancellationToken);

            double? probability;
            try { probability = classifier.Score(image); }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Classifier failed for {Filename}", request.Filename);
                probability = null;
            }

            var band = DetectionPolicy.BandFor(probability, settings);
            var overall = DetectionPolicy.Overall(probability, band, c2pa);
            var (thumbnail, thumbnailWidth, thumbnailHeight) = MakeThumbnail(image);
            var storage = new StorageInfo();

            var result = new AnalysisResult
            {
                CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString() : request.CorrelationId,
                ProcessedAtUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                ProcessingRegion = settings.Region,
                Model = classifier.Description,
                Email = new EmailInfo(request.Subject, request.Sender, request.ReceivedAtUtc),
                Image = new ImageInfo(request.Filename, request.Data.LongLength, sha256, image.Width, image.Height, new ThumbnailSize(thumbnailWidth, thumbnailHeight)),
                Detection = new DetectionInfo(probability, band, new Thresholds(settings.BandLow, settings.BandHigh)),
                Overall = overall,
                Provenance = new ProvenanceInfo(c2pa),
                Metadata = metadata,
                Storage = storage
            };

            try { storage.ThumbnailBlob = await store.SaveThumbnailAsync(sha256, thumbnail, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Thumbnail storage failed");
                storage.Error = exception.Message;
            }

            result.Evidence = BuildEvidence(result, storage.ThumbnailBlob is not null);
            try { storage.RecordWritten = await store.WriteRecordAsync(result, cancellationToken); }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Result storage failed");
                storage.Error = exception.Message;
            }

            logger.LogInformation(
                "audit event correlation_id={CorrelationId} processed_at={ProcessedAt} image_sha256={Sha256} band={Band} model={Model} version={Version} region={Region} stored={Stored}",
                result.CorrelationId, result.ProcessedAtUtc, sha256, band, result.Model.Name, result.Model.Version,
                result.ProcessingRegion, storage.ThumbnailBlob is not null);
            return result;
        }
    }

    private (byte[] Data, int Width, int Height) MakeThumbnail(Image<Rgb24> image)
    {
        using var thumbnail = image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(settings.ThumbnailMaxPixels, settings.ThumbnailMaxPixels),
            Sampler = KnownResamplers.Lanczos3
        }));
        using var stream = new MemoryStream();
        thumbnail.Save(stream, new JpegEncoder { Quality = 85 });
        return (stream.ToArray(), thumbnail.Width, thumbnail.Height);
    }

    private static List<string> BuildEvidence(AnalysisResult result, bool thumbnailStored)
    {
        var evidence = new List<string> { $"Overall indicator: {result.Overall.Label} (basis: {result.Overall.Basis})." };
        if (result.Detection.AiProbability is null)
            evidence.Add("AI-generation classifier did not return a score.");
        else
            evidence.Add($"AI-generation classifier ({result.Model.Name}) scored {result.Detection.AiProbability:P0} ({result.Detection.Band}). This is a review signal, not a determination that the image is fake or genuine.");

        var c2pa = result.Provenance.C2pa;
        if (c2pa.Present)
        {
            var line = $"C2PA Content Credentials present; validation state: {c2pa.ValidationState}.";
            if (!string.IsNullOrWhiteSpace(c2pa.DigitalSourceType)) line += $" Declared digital source type: {c2pa.DigitalSourceType}.";
            if (c2pa.Issues.Count > 0) line += $" Validation codes: {string.Join(", ", c2pa.Issues)}.";
            evidence.Add(line);
        }
        else if (!string.IsNullOrWhiteSpace(c2pa.Error))
            evidence.Add($"C2PA Content Credentials could not be verified: {c2pa.Error}.");
        else
            evidence.Add("No C2PA Content Credentials present; provenance is not cryptographically embedded.");

        evidence.AddRange(result.Metadata.Findings);
        evidence.Add(thumbnailStored
            ? "Only a downscaled thumbnail was persisted; the original image was processed transiently and discarded."
            : "No image data was persisted by the detector.");
        return evidence;
    }
}
