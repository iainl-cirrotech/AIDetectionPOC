using AiDetection.Web;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
var settings = new AppSettings();
builder.Services.AddSingleton(settings);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonDefaults.Options.PropertyNamingPolicy;
    options.SerializerOptions.DictionaryKeyPolicy = JsonDefaults.Options.DictionaryKeyPolicy;
});
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = settings.MaxImageBytes);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = settings.MaxImageBytes);
builder.Services.AddSingleton<IImageClassifier, ImageClassifier>();
builder.Services.AddSingleton<C2paVerifier>();
builder.Services.AddSingleton<IResultStore>(_ => settings.StoreBackend == "azure"
    ? new AzureResultStore(settings)
    : new FileResultStore(settings));
builder.Services.AddSingleton<AnalysisService>();
builder.Services.AddHostedService<RetentionWorker>();
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
    builder.Services.AddOpenTelemetry().UseAzureMonitor();

var app = builder.Build();

app.MapGet("/", async (IResultStore store, CancellationToken token) =>
    Results.Content(HtmlPage.Render(await store.GetRecordsAsync(settings.MaxRecords, token), settings), "text/html; charset=utf-8"));

app.MapGet("/health", (IImageClassifier classifier, IResultStore store) => Results.Ok(new
{
    status = "ok",
    processing_region = settings.Region,
    classifier = classifier.Description,
    classifier_available = classifier.Available,
    storage_enabled = store.Enabled,
    storage_backend = settings.StoreBackend,
    band_thresholds = new { low = settings.BandLow, high = settings.BandHigh },
    original_persistence = false
}));

app.MapPost("/analyze", async (HttpRequest request, AnalysisService analyzer, CancellationToken token) =>
{
    var (data, filename) = await ReadImageAsync(request, settings.MaxImageBytes, token);
    try
    {
        var result = await analyzer.AnalyzeAsync(new AnalysisRequest(
            data, filename,
            request.Headers["x-email-subject"].FirstOrDefault(),
            request.Headers["x-email-sender"].FirstOrDefault(),
            request.Headers["x-email-received"].FirstOrDefault(),
            request.Headers["x-correlation-id"].FirstOrDefault()), token);
        return Results.Json(result, JsonDefaults.Options);
    }
    catch (ImageRejectedException exception) { return Results.UnprocessableEntity(new { detail = exception.Message }); }
}).AddEndpointFilter(async (context, next) =>
{
    var request = context.HttpContext.Request;
    if (!string.IsNullOrWhiteSpace(settings.ApiKey) && request.Headers["x-api-key"] != settings.ApiKey)
        return Results.Unauthorized();
    return await next(context);
});

app.MapPost("/upload", async (HttpRequest request, AnalysisService analyzer, CancellationToken token) =>
{
    if (!settings.BrowserUploadEnabled) return Results.NotFound();
    try
    {
        var form = await request.ReadFormAsync(token);
        var file = form.Files.GetFile("file") ?? throw new ImageRejectedException("select an image to analyse");
        if (file.Length > settings.MaxImageBytes) throw new ImageRejectedException("image exceeds the configured size limit");
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token);
        await analyzer.AnalyzeAsync(new AnalysisRequest(
            memory.ToArray(), file.FileName,
            form["subject"].FirstOrDefault(), form["sender"].FirstOrDefault(),
            DateTimeOffset.UtcNow.ToString("O"), null), token);
        return Results.Redirect("/");
    }
    catch (Exception exception) when (exception is ImageRejectedException or InvalidDataException)
    {
        var records = await app.Services.GetRequiredService<IResultStore>().GetRecordsAsync(settings.MaxRecords, token);
        return Results.Content(HtmlPage.Render(records, settings, exception.Message), "text/html; charset=utf-8", statusCode: 422);
    }
});

app.MapGet("/thumb/{name}", async (string name, IResultStore store, CancellationToken token) =>
{
    var data = await store.GetThumbnailAsync(name, token);
    return data is null ? Results.NotFound() : Results.File(data, "image/jpeg", enableRangeProcessing: false);
});

app.Run();

static async Task<(byte[] Data, string Filename)> ReadImageAsync(HttpRequest request, long maximum, CancellationToken token)
{
    if (request.HasFormContentType)
    {
        var form = await request.ReadFormAsync(token);
        var file = form.Files.GetFile("file") ?? throw new BadHttpRequestException("multipart body must include a 'file' field");
        if (file.Length > maximum) throw new BadHttpRequestException("image exceeds the configured size limit", StatusCodes.Status413PayloadTooLarge);
        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token);
        return (memory.ToArray(), file.FileName);
    }

    using var body = new MemoryStream();
    await request.Body.CopyToAsync(body, token);
    if (body.Length > maximum) throw new BadHttpRequestException("image exceeds the configured size limit", StatusCodes.Status413PayloadTooLarge);
    return (body.ToArray(), request.Headers["x-filename"].FirstOrDefault() ?? "attachment");
}

public partial class Program;
