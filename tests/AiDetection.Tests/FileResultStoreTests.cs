using AiDetection.Web;

namespace AiDetection.Tests;

public sealed class FileResultStoreTests
{
    [Fact]
    public async Task Purge_removes_only_expired_records()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aidetect-store-test-{Guid.NewGuid():N}");
        try
        {
            var store = new FileResultStore(new AppSettings { LocalStoreDirectory = directory });
            await store.WriteRecordAsync(Result("old", "2026-01-01T00:00:00Z"), CancellationToken.None);
            await store.WriteRecordAsync(Result("new", "2026-09-21T00:00:00Z"), CancellationToken.None);

            var count = await store.PurgeOlderThanAsync(DateTimeOffset.Parse("2026-09-01T00:00:00Z"), CancellationToken.None);
            var retained = await store.GetRecordsAsync(10, CancellationToken.None);

            Assert.Equal(1, count);
            Assert.Single(retained);
            Assert.Equal("new", retained[0].CorrelationId);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static AnalysisResult Result(string id, string processedAt) => new()
    {
        CorrelationId = id,
        ProcessedAtUtc = processedAt,
        ProcessingRegion = "test",
        Model = new("mock", "test", "1", "revision", "CPU", "test"),
        Email = new(null, null, null),
        Image = new("test.jpg", 1, id, 1, 1, new(1, 1)),
        Detection = new(0.1, "Low", new(0.35, 0.65)),
        Overall = new("Low AI-generation indication", "Low", "classifier"),
        Provenance = new(new C2paResult()),
        Metadata = new MetadataResult(),
        Storage = new StorageInfo(),
        Evidence = []
    };
}
