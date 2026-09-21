namespace AiDetection.Web;

public sealed class AppSettings
{
    public string Region { get; init; } = Env("AZURE_REGION", "uksouth");
    public string ClassifierMode { get; init; } = Env("DETECTOR_CLASSIFIER", "onnx").ToLowerInvariant();
    public string ModelPath { get; init; } = Env("DETECTOR_MODEL_PATH", Path.Combine("models", "community_forensics_frontier_fp16.onnx"));
    public string ModelId { get; init; } = Env("DETECTOR_MODEL_ID", "Thermostatic/community-forensics-frontier-detector-2026-08");
    public string ModelVersion { get; init; } = Env("DETECTOR_MODEL_VERSION", "frontier-2026-08");
    public string ModelRevision { get; init; } = Env("DETECTOR_MODEL_REVISION", "16db135220b318d811b207db576d90368980b595");
    public float CalibrationSlope { get; init; } = EnvFloat("MODEL_CALIBRATION_SLOPE", 1.0f);
    public float CalibrationIntercept { get; init; } = EnvFloat("MODEL_CALIBRATION_INTERCEPT", -0.7403358f);
    public double BandLow { get; init; } = EnvDouble("BAND_LOW_THRESHOLD", 0.35);
    public double BandHigh { get; init; } = EnvDouble("BAND_HIGH_THRESHOLD", 0.65);
    public long MaxImageBytes { get; init; } = EnvLong("MAX_IMAGE_BYTES", 20 * 1024 * 1024);
    public long MaxImagePixels { get; init; } = EnvLong("MAX_IMAGE_PIXELS", 80_000_000);
    public int ThumbnailMaxPixels { get; init; } = EnvInt("THUMBNAIL_MAX_PX", 512);
    public string StorageAccountUrl { get; init; } = Env("STORAGE_ACCOUNT_URL", "").TrimEnd('/');
    public string StoreBackend { get; init; } = Env("STORE_BACKEND", "file").ToLowerInvariant();
    public string LocalStoreDirectory { get; init; } = Env("LOCAL_STORE_DIR", Path.Combine(Path.GetTempPath(), "aidetect-dotnet-store"));
    public string ThumbnailContainer { get; init; } = Env("THUMBNAIL_CONTAINER", "thumbnails");
    public string ResultsTable { get; init; } = Env("RESULTS_TABLE", "detections");
    public int MaxRecords { get; init; } = EnvInt("MAX_RECORDS", 50);
    public int ResultsRetentionDays { get; init; } = EnvInt("RESULTS_RETENTION_DAYS", 30);
    public string ApiKey { get; init; } = Env("DETECTOR_API_KEY", "");
    public string C2paToolPath { get; init; } = Env("C2PATOOL_PATH", "c2patool");
    public string C2paTrustFile { get; init; } = Env("C2PA_TRUST_FILE", Path.Combine(AppContext.BaseDirectory, "c2pa_trust", "C2PA-TRUST-LIST.pem"));
    public bool BrowserUploadEnabled => EnvBool("ENABLE_BROWSER_UPLOAD", StoreBackend == "file");

    private static string Env(string name, string fallback) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;
    private static int EnvInt(string name, int fallback) => int.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    private static long EnvLong(string name, long fallback) => long.TryParse(Environment.GetEnvironmentVariable(name), out var value) ? value : fallback;
    private static float EnvFloat(string name, float fallback) => float.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static double EnvDouble(string name, double fallback) => double.TryParse(Environment.GetEnvironmentVariable(name), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
    private static bool EnvBool(string name, bool fallback) => Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() switch
    {
        "1" or "true" or "yes" or "on" => true,
        "0" or "false" or "no" or "off" => false,
        _ => fallback
    };
}
