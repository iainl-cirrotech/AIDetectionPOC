using AiDetection.Web;

namespace AiDetection.Tests;

public sealed class ResultRecordTests
{
    [Fact]
    public void Flattened_record_retains_audit_fields()
    {
        var result = new AnalysisResult
        {
            CorrelationId = "test-id",
            ProcessedAtUtc = "2026-09-21T12:00:00Z",
            ProcessingRegion = "uksouth",
            Model = new("onnx", "model", "version", "revision", "CPU", "architecture"),
            Email = new("subject", "sender", "received"),
            Image = new("image.jpg", 123, "abc", 10, 20, new(10, 20)),
            Detection = new(0.75, "High", new(0.35, 0.65)),
            Overall = new("Manual review recommended - high classifier score", "High", "classifier"),
            Provenance = new(new C2paResult()),
            Metadata = new MetadataResult { Findings = ["finding"] },
            Storage = new StorageInfo { ThumbnailBlob = "abc.jpg" },
            Evidence = ["evidence"]
        };

        var record = ResultRecord.From(result);
        Assert.Equal("test-id", record.CorrelationId);
        Assert.Equal(0.75, record.AiProbability);
        Assert.Equal("abc.jpg", record.ThumbnailBlob);
        Assert.Equal(["evidence"], record.Evidence);
        Assert.Equal(["finding"], record.Findings);
    }
}
