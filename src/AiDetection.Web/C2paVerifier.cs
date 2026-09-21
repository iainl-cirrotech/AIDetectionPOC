using System.Diagnostics;
using System.Text.Json;

namespace AiDetection.Web;

public sealed class C2paVerifier(AppSettings settings, ILogger<C2paVerifier> logger)
{
    private static readonly string[] NoManifestHints = ["no jumbf", "no claim", "not found", "no manifest", "missing", "unrecognized", "no supported"];

    public async Task<C2paResult> VerifyAsync(byte[] raw, string filename, CancellationToken cancellationToken)
    {
        var result = new C2paResult { TrustConfigured = File.Exists(settings.C2paTrustFile) };
        var extension = Path.GetExtension(filename);
        if (extension.Length > 10 || extension.Any(character => !char.IsLetterOrDigit(character) && character != '.')) extension = ".bin";
        var tempPath = Path.Combine(Path.GetTempPath(), $"aidetect-c2pa-{Guid.NewGuid():N}{extension}");

        try
        {
            await File.WriteAllBytesAsync(tempPath, raw, cancellationToken);
            var startInfo = new ProcessStartInfo
            {
                FileName = settings.C2paToolPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(tempPath);
            if (result.TrustConfigured)
            {
                startInfo.ArgumentList.Add("trust");
                startInfo.ArgumentList.Add("--trust_anchors");
                startInfo.ArgumentList.Add(settings.C2paTrustFile);
            }

            using var process = Process.Start(startInfo);
            if (process is null) throw new InvalidOperationException("c2patool did not start");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (string.IsNullOrWhiteSpace(stdout))
            {
                var message = string.IsNullOrWhiteSpace(stderr) ? $"c2patool exited with code {process.ExitCode}" : stderr.Trim();
                if (NoManifestHints.Any(hint => message.Contains(hint, StringComparison.OrdinalIgnoreCase))) return result;
                result.Error = message;
                return result;
            }

            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;
            result.Present = HasManifest(root);
            if (!result.Present) return result;

            result.Issues = FindProperties(root, "validation_status")
                .SelectMany(ReadStatusCodes)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            result.ValidationState = FindFirstString(root, "validation_state")
                ?? (result.Issues.Count > 0 ? "invalid" : "valid");
            result.DigitalSourceType = FindFirstString(root, "digitalSourceType");
            return result;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            result.Error = $"c2patool is unavailable at '{settings.C2paToolPath}'";
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "C2PA verification failed");
            var message = exception.Message;
            if (!NoManifestHints.Any(hint => message.Contains(hint, StringComparison.OrdinalIgnoreCase))) result.Error = message;
            return result;
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort secure lifecycle */ }
        }
    }

    private static bool HasManifest(JsonElement root)
        => FindProperties(root, "active_manifest").Any(value => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
           || FindProperties(root, "manifests").Any(value => value.ValueKind == JsonValueKind.Object && value.EnumerateObject().Any());

    private static string? FindFirstString(JsonElement root, string name)
    {
        foreach (var value in FindProperties(root, name))
            if (value.ValueKind == JsonValueKind.String) return value.GetString();
        return null;
    }

    private static IEnumerable<JsonElement> FindProperties(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.NameEquals(name)) yield return property.Value;
                foreach (var nested in FindProperties(property.Value, name)) yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var nested in FindProperties(item, name)) yield return nested;
        }
    }

    private static IEnumerable<string> ReadStatusCodes(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var code in ReadStatusCodes(item)) yield return code;
        }
        else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
        {
            if (code.GetString() is { Length: > 0 } value) yield return value;
        }
        else if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value)
        {
            yield return value;
        }
    }
}
