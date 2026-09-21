namespace AiDetection.Web;

public static class DetectionPolicy
{
    private static readonly string[] AiSourceTypes = ["trainedalgorithmicmedia", "compositewithtrainedalgorithmicmedia"];
    private static readonly string[] TamperCodes = ["datahash.mismatch", "claimsignature.mismatch", "boxeshash.mismatch"];

    public static string BandFor(double? probability, AppSettings settings)
    {
        if (probability is null) return "Unknown";
        if (probability >= settings.BandHigh) return "High";
        if (probability >= settings.BandLow) return "Medium";
        return "Low";
    }

    public static OverallIndicator Overall(double? probability, string band, C2paResult c2pa)
    {
        if (c2pa.Present)
        {
            var state = c2pa.ValidationState?.ToLowerInvariant() ?? "";
            var source = c2pa.DigitalSourceType?.ToLowerInvariant() ?? "";
            var issues = c2pa.Issues.Select(value => value.ToLowerInvariant()).ToArray();

            if (AiSourceTypes.Any(source.Contains))
            {
                var label = source.Contains("compositewithtrainedalgorithmicmedia")
                    ? "AI-modified (provenance)"
                    : "AI-generated (provenance)";
                if (issues.Contains("signingcredential.untrusted")) label += " - signer unverified";
                return new(label, "High", "c2pa");
            }

            if (state == "invalid" || issues.Any(TamperCodes.Contains))
                return new("Provenance invalid - possible alteration", "Medium", "c2pa");
        }

        if (probability is null) return new("Inconclusive", "Unknown", "none");
        return band switch
        {
            "High" => new("High AI-generation indication", "High", "classifier"),
            "Medium" => new("Review recommended - AI indication detected", "Medium", "classifier"),
            "Low" => new("Low AI-generation indication", "Low", "classifier"),
            _ => new("Inconclusive", "Unknown", "none")
        };
    }
}
