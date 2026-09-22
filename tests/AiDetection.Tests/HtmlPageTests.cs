using AiDetection.Web;

namespace AiDetection.Tests;

public sealed class HtmlPageTests
{
    [Fact]
    public void Render_rewords_legacy_classifier_only_results()
    {
        var record = new ResultRecord
        {
            OverallLabel = "High AI-generation indication",
            OverallSeverity = "High",
            OverallBasis = "classifier",
            AiProbability = 0.91,
            Band = "High",
            Evidence =
            [
                "Overall indicator: High AI-generation indication (basis: classifier).",
                "AI-generation detector (model) scored 91% (High). This is a screening score, not proof of authenticity."
            ]
        };

        var html = HtmlPage.Render([record], new AppSettings());

        Assert.Contains("Manual review recommended - high classifier score", html);
        Assert.Contains("This is a review signal, not a determination that the image is fake or genuine.", html);
        Assert.Contains("&copy; 2026 Cirrotech Ltd. All rights reserved.", html);
        Assert.DoesNotContain("High AI-generation indication", html);
    }

    [Fact]
    public void Render_includes_copyright_when_there_are_no_results()
    {
        var html = HtmlPage.Render([], new AppSettings());

        Assert.Contains("&copy; 2026 Cirrotech Ltd. All rights reserved.", html);
    }
}
