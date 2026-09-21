using System.Text.Json.Serialization;

namespace AiDetection.Web;

public sealed record ModelDescription(string Mode, string Name, string Version, string Revision, string Device, string Architecture);
public sealed record EmailInfo(string? Subject, string? Sender, string? ReceivedAtUtc);
public sealed record ThumbnailSize(int Width, int Height);
public sealed record ImageInfo(string Filename, long SizeBytes, string Sha256, int Width, int Height, ThumbnailSize ThumbnailSize);
public sealed record DetectionInfo(double? AiProbability, string Band, Thresholds Thresholds);
public sealed record Thresholds(double Low, double High);
public sealed record OverallIndicator(string Label, string Severity, string Basis);

public sealed class C2paResult
{
    public bool Present { get; set; }
    public string? ValidationState { get; set; }
    public string? DigitalSourceType { get; set; }
    public List<string> Issues { get; set; } = [];
    public bool TrustConfigured { get; set; }
    public string? Error { get; set; }
}

public sealed record ProvenanceInfo(C2paResult C2pa);

public sealed class MetadataResult
{
    public bool ExifPresent { get; set; }
    public bool XmpPresent { get; set; }
    public bool C2paMarkerPresent { get; set; }
    public string? Camera { get; set; }
    public string? Software { get; set; }
    public List<string> Findings { get; set; } = [];
}

public sealed class StorageInfo
{
    public string? ThumbnailBlob { get; set; }
    public bool RecordWritten { get; set; }
    public bool OriginalPersisted { get; set; }
    public string? Error { get; set; }
}

public sealed class AnalysisResult
{
    public required string CorrelationId { get; init; }
    public required string ProcessedAtUtc { get; init; }
    public required string ProcessingRegion { get; init; }
    public required ModelDescription Model { get; init; }
    public required EmailInfo Email { get; init; }
    public required ImageInfo Image { get; init; }
    public required DetectionInfo Detection { get; init; }
    public required OverallIndicator Overall { get; init; }
    public required ProvenanceInfo Provenance { get; init; }
    public required MetadataResult Metadata { get; init; }
    public required StorageInfo Storage { get; init; }
    public List<string> Evidence { get; set; } = [];
}

public sealed class ResultRecord
{
    public string CorrelationId { get; set; } = "";
    public string ProcessedAtUtc { get; set; } = "";
    public string ReceivedAtUtc { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Sender { get; set; } = "";
    public string Filename { get; set; } = "";
    public string ImageSha256 { get; set; } = "";
    public string ThumbnailBlob { get; set; } = "";
    public double? AiProbability { get; set; }
    public string Band { get; set; } = "";
    public string OverallLabel { get; set; } = "";
    public string OverallSeverity { get; set; } = "";
    public string OverallBasis { get; set; } = "";
    public bool C2paPresent { get; set; }
    public string C2paState { get; set; } = "";
    public bool C2paTrustConfigured { get; set; }
    public string DigitalSourceType { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string ModelVersion { get; set; } = "";
    public string ProcessingRegion { get; set; } = "";
    public List<string> Evidence { get; set; } = [];
    public List<string> Findings { get; set; } = [];

    public static ResultRecord From(AnalysisResult result) => new()
    {
        CorrelationId = result.CorrelationId,
        ProcessedAtUtc = result.ProcessedAtUtc,
        ReceivedAtUtc = result.Email.ReceivedAtUtc ?? "",
        Subject = result.Email.Subject ?? "",
        Sender = result.Email.Sender ?? "",
        Filename = result.Image.Filename,
        ImageSha256 = result.Image.Sha256,
        ThumbnailBlob = result.Storage.ThumbnailBlob ?? "",
        AiProbability = result.Detection.AiProbability,
        Band = result.Detection.Band,
        OverallLabel = result.Overall.Label,
        OverallSeverity = result.Overall.Severity,
        OverallBasis = result.Overall.Basis,
        C2paPresent = result.Provenance.C2pa.Present,
        C2paState = result.Provenance.C2pa.ValidationState ?? "",
        C2paTrustConfigured = result.Provenance.C2pa.TrustConfigured,
        DigitalSourceType = result.Provenance.C2pa.DigitalSourceType ?? "",
        ModelName = result.Model.Name,
        ModelVersion = result.Model.Version,
        ProcessingRegion = result.ProcessingRegion,
        Evidence = result.Evidence,
        Findings = result.Metadata.Findings
    };
}
