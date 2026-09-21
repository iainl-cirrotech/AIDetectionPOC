using AiDetection.Web;

namespace AiDetection.Tests;

public sealed class DetectionPolicyTests
{
    private readonly AppSettings settings = new();

    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(0.1, "Low")]
    [InlineData(0.35, "Medium")]
    [InlineData(0.649, "Medium")]
    [InlineData(0.65, "High")]
    public void Band_thresholds_match_the_existing_detector(double? probability, string expected)
        => Assert.Equal(expected, DetectionPolicy.BandFor(probability, settings));

    [Fact]
    public void Ai_c2pa_provenance_takes_precedence_over_classifier()
    {
        var c2pa = new C2paResult
        {
            Present = true,
            ValidationState = "valid",
            DigitalSourceType = "http://cv.iptc.org/newscodes/digitalsourcetype/trainedAlgorithmicMedia"
        };
        var result = DetectionPolicy.Overall(0.01, "Low", c2pa);
        Assert.Equal("AI-generated (provenance)", result.Label);
        Assert.Equal("High", result.Severity);
        Assert.Equal("c2pa", result.Basis);
    }

    [Fact]
    public void Classifier_drives_indicator_when_provenance_is_absent()
    {
        var result = DetectionPolicy.Overall(0.8, "High", new C2paResult());
        Assert.Equal("High AI-generation indication", result.Label);
        Assert.Equal("classifier", result.Basis);
    }

    [Fact]
    public void Invalid_provenance_is_flagged_even_with_low_classifier_score()
    {
        var result = DetectionPolicy.Overall(0.1, "Low", new C2paResult
        {
            Present = true,
            ValidationState = "invalid",
            Issues = ["dataHash.mismatch"]
        });
        Assert.Equal("Provenance invalid - possible alteration", result.Label);
        Assert.Equal("Medium", result.Severity);
    }
}
