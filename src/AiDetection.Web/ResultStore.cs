using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace AiDetection.Web;

public interface IResultStore
{
    bool Enabled { get; }
    Task<string?> SaveThumbnailAsync(string sha256, byte[] data, CancellationToken cancellationToken);
    Task<bool> WriteRecordAsync(AnalysisResult result, CancellationToken cancellationToken);
    Task<IReadOnlyList<ResultRecord>> GetRecordsAsync(int maximum, CancellationToken cancellationToken);
    Task<byte[]?> GetThumbnailAsync(string name, CancellationToken cancellationToken);
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public sealed class FileResultStore : IResultStore
{
    private readonly string directory;
    private readonly string thumbnailDirectory;
    private readonly string recordsPath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public FileResultStore(AppSettings settings)
    {
        directory = Path.GetFullPath(settings.LocalStoreDirectory);
        thumbnailDirectory = Path.Combine(directory, "thumbnails");
        recordsPath = Path.Combine(directory, "detections.jsonl");
        Directory.CreateDirectory(thumbnailDirectory);
    }

    public bool Enabled => true;

    public async Task<string?> SaveThumbnailAsync(string sha256, byte[] data, CancellationToken cancellationToken)
    {
        var name = $"{sha256}.jpg";
        await File.WriteAllBytesAsync(Path.Combine(thumbnailDirectory, name), data, cancellationToken);
        return name;
    }

    public async Task<bool> WriteRecordAsync(AnalysisResult result, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(ResultRecord.From(result), JsonDefaults.Options) + Environment.NewLine;
        await gate.WaitAsync(cancellationToken);
        try { await File.AppendAllTextAsync(recordsPath, line, cancellationToken); }
        finally { gate.Release(); }
        return true;
    }

    public async Task<IReadOnlyList<ResultRecord>> GetRecordsAsync(int maximum, CancellationToken cancellationToken)
    {
        if (!File.Exists(recordsPath)) return [];
        await gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await File.ReadAllLinesAsync(recordsPath, cancellationToken);
            return lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize<ResultRecord>(line, JsonDefaults.Options))
                .OfType<ResultRecord>()
                .OrderByDescending(record => record.ProcessedAtUtc, StringComparer.Ordinal)
                .Take(maximum)
                .ToArray();
        }
        finally { gate.Release(); }
    }

    public async Task<byte[]?> GetThumbnailAsync(string name, CancellationToken cancellationToken)
    {
        var safeName = Path.GetFileName(name);
        var path = Path.Combine(thumbnailDirectory, safeName);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        if (!File.Exists(recordsPath)) return 0;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await File.ReadAllLinesAsync(recordsPath, cancellationToken);
            var parsed = lines.Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => (Line: line, Record: JsonSerializer.Deserialize<ResultRecord>(line, JsonDefaults.Options)))
                .Where(item => item.Record is not null)
                .ToArray();
            var expired = parsed.Where(item => DateTimeOffset.TryParse(item.Record!.ProcessedAtUtc, out var timestamp) && timestamp < cutoff).ToArray();
            if (expired.Length == 0) return 0;
            var retained = parsed.Except(expired).ToArray();
            var tempPath = recordsPath + $".{Guid.NewGuid():N}.tmp";
            await File.WriteAllLinesAsync(tempPath, retained.Select(item => item.Line), cancellationToken);
            File.Move(tempPath, recordsPath, overwrite: true);
            var retainedThumbnails = retained.Select(item => item.Record!.ThumbnailBlob).ToHashSet(StringComparer.Ordinal);
            foreach (var item in expired)
                if (!string.IsNullOrWhiteSpace(item.Record!.ThumbnailBlob) && !retainedThumbnails.Contains(item.Record.ThumbnailBlob))
                    File.Delete(Path.Combine(thumbnailDirectory, Path.GetFileName(item.Record.ThumbnailBlob)));
            return expired.Length;
        }
        finally { gate.Release(); }
    }
}

public sealed class AzureResultStore : IResultStore
{
    private readonly BlobContainerClient blobs;
    private readonly TableClient table;

    public AzureResultStore(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.StorageAccountUrl))
            throw new InvalidOperationException("STORAGE_ACCOUNT_URL is required for the azure store backend");
        var credential = new DefaultAzureCredential();
        blobs = new BlobContainerClient(new Uri($"{settings.StorageAccountUrl}/{settings.ThumbnailContainer}"), credential);
        var tableEndpoint = settings.StorageAccountUrl.Replace(".blob.", ".table.", StringComparison.OrdinalIgnoreCase);
        table = new TableClient(new Uri(tableEndpoint), settings.ResultsTable, credential);
    }

    public bool Enabled => true;

    public async Task<string?> SaveThumbnailAsync(string sha256, byte[] data, CancellationToken cancellationToken)
    {
        var name = $"{sha256}.jpg";
        await using var stream = new MemoryStream(data);
        var blob = blobs.GetBlobClient(name);
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
        await blob.SetHttpHeadersAsync(new BlobHttpHeaders { ContentType = "image/jpeg" }, cancellationToken: cancellationToken);
        return name;
    }

    public async Task<bool> WriteRecordAsync(AnalysisResult result, CancellationToken cancellationToken)
    {
        var record = ResultRecord.From(result);
        var entity = new TableEntity(result.ProcessedAtUtc[..10].Replace("-", ""), result.CorrelationId)
        {
            ["correlation_id"] = record.CorrelationId,
            ["processed_at_utc"] = record.ProcessedAtUtc,
            ["received_at_utc"] = record.ReceivedAtUtc,
            ["subject"] = record.Subject,
            ["sender"] = record.Sender,
            ["filename"] = record.Filename,
            ["image_sha256"] = record.ImageSha256,
            ["thumbnail_blob"] = record.ThumbnailBlob,
            ["band"] = record.Band,
            ["overall_label"] = record.OverallLabel,
            ["overall_severity"] = record.OverallSeverity,
            ["overall_basis"] = record.OverallBasis,
            ["c2pa_present"] = record.C2paPresent,
            ["c2pa_state"] = record.C2paState,
            ["c2pa_trust_configured"] = record.C2paTrustConfigured,
            ["digital_source_type"] = record.DigitalSourceType,
            ["model_name"] = record.ModelName,
            ["model_version"] = record.ModelVersion,
            ["processing_region"] = record.ProcessingRegion,
            ["evidence_json"] = JsonSerializer.Serialize(record.Evidence),
            ["findings_json"] = JsonSerializer.Serialize(record.Findings)
        };
        if (record.AiProbability is not null) entity["ai_probability"] = record.AiProbability.Value;
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ResultRecord>> GetRecordsAsync(int maximum, CancellationToken cancellationToken)
    {
        var records = new List<ResultRecord>();
        await foreach (var entity in table.QueryAsync<TableEntity>(maxPerPage: maximum, cancellationToken: cancellationToken))
            records.Add(ToRecord(entity));
        return records.OrderByDescending(record => record.ProcessedAtUtc, StringComparer.Ordinal).Take(maximum).ToArray();
    }

    public async Task<byte[]?> GetThumbnailAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var response = await blobs.GetBlobClient(Path.GetFileName(name)).DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404) { return null; }
    }

    public async Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var count = 0;
        await foreach (var entity in table.QueryAsync<TableEntity>(cancellationToken: cancellationToken))
        {
            var timestamp = entity.Timestamp;
            if (timestamp is null || timestamp >= cutoff) continue;
            await table.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, ETag.All, cancellationToken);
            count++;
        }
        return count;
    }

    private static ResultRecord ToRecord(TableEntity entity) => new()
    {
        CorrelationId = Get<string>(entity, "correlation_id") ?? entity.RowKey,
        ProcessedAtUtc = Get<string>(entity, "processed_at_utc") ?? "",
        ReceivedAtUtc = Get<string>(entity, "received_at_utc") ?? "",
        Subject = Get<string>(entity, "subject") ?? "",
        Sender = Get<string>(entity, "sender") ?? "",
        Filename = Get<string>(entity, "filename") ?? "",
        ImageSha256 = Get<string>(entity, "image_sha256") ?? "",
        ThumbnailBlob = Get<string>(entity, "thumbnail_blob") ?? "",
        AiProbability = Get<double?>(entity, "ai_probability"),
        Band = Get<string>(entity, "band") ?? "",
        OverallLabel = Get<string>(entity, "overall_label") ?? "",
        OverallSeverity = Get<string>(entity, "overall_severity") ?? "",
        OverallBasis = Get<string>(entity, "overall_basis") ?? "",
        C2paPresent = Get<bool?>(entity, "c2pa_present") ?? false,
        C2paState = Get<string>(entity, "c2pa_state") ?? "",
        C2paTrustConfigured = Get<bool?>(entity, "c2pa_trust_configured") ?? false,
        DigitalSourceType = Get<string>(entity, "digital_source_type") ?? "",
        ModelName = Get<string>(entity, "model_name") ?? "",
        ModelVersion = Get<string>(entity, "model_version") ?? "",
        ProcessingRegion = Get<string>(entity, "processing_region") ?? "",
        Evidence = DeserializeList(Get<string>(entity, "evidence_json")),
        Findings = DeserializeList(Get<string>(entity, "findings_json"))
    };

    private static T? Get<T>(TableEntity entity, string key) => entity.TryGetValue(key, out var value) && value is T typed ? typed : default;
    private static List<string> DeserializeList(string? value) => string.IsNullOrWhiteSpace(value) ? [] : JsonSerializer.Deserialize<List<string>>(value) ?? [];
}
