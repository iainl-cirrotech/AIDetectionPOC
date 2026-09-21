using System.Security.Cryptography;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AiDetection.Web;

public interface IImageClassifier
{
    ModelDescription Description { get; }
    bool Available { get; }
    double? Score(Image<Rgb24> image);
}

public sealed class ImageClassifier : IImageClassifier, IDisposable
{
    private const int ResizeShortEdge = 440;
    private const int InputSize = 384;
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StandardDeviation = [0.229f, 0.224f, 0.225f];
    private readonly AppSettings settings;
    private readonly ILogger<ImageClassifier> logger;
    private readonly Lazy<InferenceSession?> session;

    public ImageClassifier(AppSettings settings, ILogger<ImageClassifier> logger)
    {
        this.settings = settings;
        this.logger = logger;
        session = new Lazy<InferenceSession?>(LoadSession, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public ModelDescription Description => new(
        settings.ClassifierMode,
        settings.ModelId,
        settings.ModelVersion,
        settings.ModelRevision,
        "CPU",
        "Community Forensics ViT-S/16 ONNX (384px)");

    public bool Available => settings.ClassifierMode switch
    {
        "disabled" => false,
        "mock" => true,
        _ => File.Exists(settings.ModelPath) && session.Value is not null
    };

    public double? Score(Image<Rgb24> image)
    {
        if (settings.ClassifierMode == "disabled") return null;
        if (settings.ClassifierMode == "mock") return MockScore(image);

        var activeSession = session.Value;
        if (activeSession is null) return null;

        using var prepared = PrepareImage(image);
        var tensor = new DenseTensor<float>([1, 3, InputSize, InputSize]);
        prepared.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < InputSize; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < InputSize; x++)
                {
                    var pixel = row[x];
                    tensor[0, 0, y, x] = (pixel.R / 255f - Mean[0]) / StandardDeviation[0];
                    tensor[0, 1, y, x] = (pixel.G / 255f - Mean[1]) / StandardDeviation[1];
                    tensor[0, 2, y, x] = (pixel.B / 255f - Mean[2]) / StandardDeviation[2];
                }
            }
        });

        var inputName = activeSession.InputMetadata.Keys.First();
        using var outputs = activeSession.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)]);
        var rawLogit = outputs.First().AsEnumerable<float>().First();
        var calibratedLogit = rawLogit * settings.CalibrationSlope + settings.CalibrationIntercept;
        return 1d / (1d + Math.Exp(-calibratedLogit));
    }

    public static Image<Rgb24> PrepareImage(Image<Rgb24> source)
    {
        var scale = ResizeShortEdge / (double)Math.Min(source.Width, source.Height);
        // torchvision Resize(int) floors the calculated long edge and uses
        // antialiased bilinear interpolation for PIL input.
        var width = Math.Max(InputSize, (int)(source.Width * scale));
        var height = Math.Max(InputSize, (int)(source.Height * scale));
        var x = (width - InputSize) / 2;
        var y = (height - InputSize) / 2;
        return source.Clone(context => context
            .Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Triangle })
            .Crop(new Rectangle(x, y, InputSize, InputSize)));
    }

    private InferenceSession? LoadSession()
    {
        if (settings.ClassifierMode is "mock" or "disabled") return null;
        if (!File.Exists(settings.ModelPath))
        {
            logger.LogWarning("ONNX model is missing at {ModelPath}; classifier results will be inconclusive", settings.ModelPath);
            return null;
        }

        try
        {
            return new InferenceSession(settings.ModelPath, new Microsoft.ML.OnnxRuntime.SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not load ONNX model at {ModelPath}", settings.ModelPath);
            return null;
        }
    }

    private static double MockScore(Image<Rgb24> image)
    {
        using var stream = new MemoryStream();
        image.SaveAsBmp(stream);
        var digest = SHA256.HashData(stream.ToArray().AsSpan(0, Math.Min(4096, (int)stream.Length)));
        return BitConverter.ToUInt32(digest, 0) / (double)uint.MaxValue;
    }

    public void Dispose()
    {
        if (session.IsValueCreated) session.Value?.Dispose();
    }
}
