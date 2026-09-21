using System.Net;
using System.Text;

namespace AiDetection.Web;

public static class HtmlPage
{
    public static string Render(IReadOnlyList<ResultRecord> records, AppSettings settings, string? error = null)
    {
        static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
        var html = new StringBuilder("""
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>AI Image Detection</title><style>
:root{color-scheme:light}body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;margin:2rem;line-height:1.45;color:#18202a;background:#fafafa}h1{font-size:1.5rem;margin:0}.muted{color:#647080;font-size:.9rem}.upload{margin:1.2rem 0;padding:1rem;background:#fff;border:1px solid #d8dee7;border-radius:8px;display:flex;gap:.7rem;align-items:end;flex-wrap:wrap}.upload label{display:grid;gap:.2rem;font-size:.85rem}.upload input{max-width:20rem}.upload button{padding:.55rem .9rem;background:#155eef;color:white;border:0;border-radius:5px;cursor:pointer}.error{background:#fde7e7;color:#8a1f11;padding:.7rem;border-radius:6px}table{border-collapse:collapse;width:100%;font-size:.88rem;background:#fff}th,td{border:1px solid #d8dee7;padding:.6rem;vertical-align:top;text-align:left}th{background:#f1f4f8;position:sticky;top:0}.thumb{max-width:150px;max-height:150px;border-radius:4px;display:block;margin-bottom:.35rem}code{font-size:.72rem;word-break:break-all;color:#596579}ul{margin:.2rem 0;padding-left:1.1rem}.pill{display:inline-block;padding:.25rem .55rem;border-radius:6px;font-weight:700}.High{background:#f8d7da;color:#7a1113}.Medium{background:#ffe8a1;color:#6b5200}.Low{background:#d6ecd2;color:#245018}.Unknown{background:#e8e8e8;color:#444}.score{font-size:1.2rem;font-weight:700}@media(max-width:900px){body{margin:1rem}table,thead,tbody,tr,th,td{display:block}thead{display:none}tr{margin-bottom:1rem;border:1px solid #ccd3dd}td{border:0;border-bottom:1px solid #e5e8ed}}
</style></head><body>
""");
        html.Append($"<header><h1>Emailed image – AI / alteration detection</h1><div class=\"muted\">Processing region: {E(settings.Region)} · Results are screening indicators, not proof.</div></header>");
        if (settings.BrowserUploadEnabled) html.Append("""
<form class="upload" action="/upload" method="post" enctype="multipart/form-data">
<label>Image<input type="file" name="file" accept="image/*" required></label>
<label>Subject (optional)<input name="subject" placeholder="Claim image"></label>
<label>Sender (optional)<input name="sender" placeholder="person@example.com"></label>
<button type="submit">Analyse image</button></form>
""");
        if (!string.IsNullOrWhiteSpace(error)) html.Append($"<p class=\"error\">{E(error)}</p>");
        if (records.Count == 0)
        {
            html.Append(settings.BrowserUploadEnabled
                ? "<p class=\"muted\">No images have been processed yet. Upload one above.</p></body></html>"
                : "<p class=\"muted\">No images have been processed yet.</p></body></html>");
            return html.ToString();
        }

        html.Append("<table><thead><tr><th>Source</th><th>Image</th><th>Detection result</th><th>Evidence</th><th>Processing / audit</th></tr></thead><tbody>");
        foreach (var record in records)
        {
            var probability = record.AiProbability is null ? "n/a" : record.AiProbability.Value.ToString("P0");
            html.Append("<tr><td>")
                .Append($"<strong>{E(string.IsNullOrWhiteSpace(record.Subject) ? "(local upload)" : record.Subject)}</strong><br><span class=\"muted\">{E(record.Sender)}</span><br><span class=\"muted\">Received: {E(record.ReceivedAtUtc)}</span></td>")
                .Append("<td>");
            if (!string.IsNullOrWhiteSpace(record.ThumbnailBlob)) html.Append($"<img class=\"thumb\" src=\"/thumb/{Uri.EscapeDataString(record.ThumbnailBlob)}\" alt=\"thumbnail\">");
            html.Append($"{E(record.Filename)}<br><code>SHA-256 {E(record.ImageSha256)}</code></td>")
                .Append($"<td><span class=\"pill {E(record.OverallSeverity)}\">{E(record.OverallLabel)}</span><div class=\"muted\">basis: {E(record.OverallBasis)}</div><div class=\"score\">{probability} ({E(record.Band)})</div><div>C2PA: {(record.C2paPresent ? "present" : "absent")} {E(record.C2paState)}</div></td>")
                .Append("<td><ul>");
            foreach (var item in record.Evidence) html.Append($"<li>{E(item)}</li>");
            html.Append($"</ul></td><td>Processed: {E(record.ProcessedAtUtc)}<br>Model: {E(record.ModelName)} v{E(record.ModelVersion)}<br>Region: {E(record.ProcessingRegion)}<br><code>id {E(record.CorrelationId)}</code></td></tr>");
        }
        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }
}
