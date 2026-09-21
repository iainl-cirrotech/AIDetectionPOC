using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace AiDetection.Web;

public static class MetadataAnalyzer
{
    private static readonly string[] EditorHints =
    [
        "photoshop", "gimp", "lightroom", "affinity", "pixelmator", "capture one", "snapseed",
        "picsart", "canva", "midjourney", "stable diffusion", "dall", "firefly", "openai",
        "bing image creator", "generative", "diffusion"
    ];

    public static MetadataResult Analyze(Image<Rgb24> image, byte[] raw)
    {
        var result = new MetadataResult();
        var ascii = Encoding.Latin1.GetString(raw);
        result.C2paMarkerPresent = ContainsAny(ascii, "c2pa", "jumb", "c2pa.claim");
        result.XmpPresent = ContainsAny(ascii, "ns.adobe.com/xap", "<x:xmpmeta", "<?xpacket");

        var exif = image.Metadata.ExifProfile;
        if (exif is not null && exif.Values.Count > 0)
        {
            result.ExifPresent = true;
            var make = FindExif(exif.Values, "Make");
            var model = FindExif(exif.Values, "Model");
            result.Software = FindExif(exif.Values, "Software");
            result.Camera = string.Join(' ', new[] { make, model }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        if (!string.IsNullOrWhiteSpace(result.Camera))
            result.Findings.Add($"Camera metadata present: {result.Camera}.");
        else if (result.ExifPresent)
            result.Findings.Add("EXIF present but no camera make/model recorded.");

        if (!string.IsNullOrWhiteSpace(result.Software))
        {
            var prefix = EditorHints.Any(hint => result.Software.Contains(hint, StringComparison.OrdinalIgnoreCase))
                ? "Editing or generation software tag detected"
                : "Software tag present";
            result.Findings.Add($"{prefix}: {result.Software}.");
        }

        if (!result.ExifPresent)
            result.Findings.Add("No EXIF metadata present; common for screenshots, messaging re-encodes, stripped files or generated images.");
        if (result.XmpPresent) result.Findings.Add("XMP metadata present.");
        if (result.C2paMarkerPresent) result.Findings.Add("C2PA/JUMBF marker found in file bytes; see provenance result.");
        return result;
    }

    private static string? FindExif(IEnumerable<SixLabors.ImageSharp.Metadata.Profiles.Exif.IExifValue> values, string name)
        => values.FirstOrDefault(value => string.Equals(value.Tag.ToString(), name, StringComparison.OrdinalIgnoreCase))
            ?.GetValue()?.ToString()?.Trim('\0', ' ');

    private static bool ContainsAny(string value, params string[] markers)
        => markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
}
