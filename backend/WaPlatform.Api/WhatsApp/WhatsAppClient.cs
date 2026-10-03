using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace WaPlatform.Api.WhatsApp;

public class WhatsAppApiException(string message, int statusCode, int? metaCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? MetaCode { get; } = metaCode;

    /// <summary>Worth retrying later: Meta/network trouble or rate limits, not a problem with the message.</summary>
    public bool IsTransient => StatusCode is 0 or 429 or >= 500
        || MetaCode is 1 or 2 or 4 or 80007 or 130429 or 131000 or 131016 or 131056 or 133004;
}

public record PhoneNumberInfo(string Id, string? DisplayPhoneNumber, string? VerifiedName, string? QualityRating);

public record MetaTemplate(string Id, string Name, string Language, string Category, string Status,
    string ParameterFormat, string ComponentsJson);

/// <summary>
/// Thin client for the Graph API. Options are read per call (IOptionsMonitor), so a rotated
/// token in configuration takes effect without a restart.
/// </summary>
public class WhatsAppClient(HttpClient http, IOptionsMonitor<WhatsAppOptions> options)
{
    private WhatsAppOptions O => options.CurrentValue;
    private string Base => $"https://graph.facebook.com/{O.GraphApiVersion}/";

    public async Task<PhoneNumberInfo> GetPhoneNumberAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get,
            $"{O.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating", null, ct);
        return new PhoneNumberInfo(
            json["id"]?.GetValue<string>() ?? O.PhoneNumberId!,
            json["display_phone_number"]?.GetValue<string>(),
            json["verified_name"]?.GetValue<string>(),
            json["quality_rating"]?.GetValue<string>());
    }

    /// <summary>All templates of the WhatsApp Business Account, following Meta's paging.</summary>
    public async Task<List<MetaTemplate>> GetTemplatesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(O.BusinessAccountId))
            throw new WhatsAppApiException("WhatsApp:BusinessAccountId is not configured.", 503, null);

        var result = new List<MetaTemplate>();
        string? url = $"{O.BusinessAccountId}/message_templates?fields=id,name,language,status,category,parameter_format,components&limit=100";
        while (url is not null)
        {
            var json = await SendAsync(HttpMethod.Get, url, null, ct);
            foreach (var t in json["data"]?.AsArray() ?? [])
            {
                result.Add(new MetaTemplate(
                    t!["id"]!.GetValue<string>(),
                    t["name"]!.GetValue<string>(),
                    t["language"]!.GetValue<string>(),
                    t["category"]?.GetValue<string>() ?? "",
                    t["status"]?.GetValue<string>() ?? "",
                    t["parameter_format"]?.GetValue<string>() ?? "POSITIONAL",
                    t["components"]?.ToJsonString() ?? "[]"));
            }
            // "next" is an absolute URL; SendAsync accepts those as-is.
            url = json["paging"]?["next"]?.GetValue<string>();
        }
        return result;
    }

    /// <summary>Sends a message (template, text, document, ...); returns Meta's message id (wamid).</summary>
    public async Task<string> SendMessageAsync(string to, string type, JsonObject content, CancellationToken ct = default)
    {
        var body = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = to,
            ["type"] = type,
            [type] = content,
        };
        var json = await SendAsync(HttpMethod.Post, $"{O.PhoneNumberId}/messages", body, ct);
        return json["messages"]?[0]?["id"]?.GetValue<string>()
            ?? throw new WhatsAppApiException("Meta accepted the request but returned no message id.", 502, null);
    }

    /// <summary>Sends an approved template with no parameters; returns Meta's message id (wamid).</summary>
    public Task<string> SendTemplateAsync(string to, string templateName, string languageCode, CancellationToken ct = default) =>
        SendMessageAsync(to, "template", new JsonObject
        {
            ["name"] = templateName,
            ["language"] = new JsonObject { ["code"] = languageCode },
        }, ct);

    /// <summary>Uploads a file to Meta; returns the media id (valid for 30 days).</summary>
    public async Task<string> UploadMediaAsync(byte[] content, string contentType, string fileName, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("whatsapp"), "messaging_product");
        form.Add(new StringContent(contentType), "type");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        var json = await SendAsync(HttpMethod.Post, $"{O.PhoneNumberId}/media", form, ct);
        return json["id"]?.GetValue<string>()
            ?? throw new WhatsAppApiException("Meta returned no media id for the upload.", 502, null);
    }

    /// <summary>Downloads media a customer sent (Meta keeps it for 30 days).</summary>
    public async Task<(byte[] Content, string ContentType)> DownloadMediaAsync(string mediaId, CancellationToken ct = default)
    {
        var info = await SendAsync(HttpMethod.Get, mediaId, null, ct);
        var url = info["url"]?.GetValue<string>()
            ?? throw new WhatsAppApiException("Meta returned no download URL for this media.", 502, null);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.AccessToken);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new WhatsAppApiException($"Media download failed (HTTP {(int)res.StatusCode}).", (int)res.StatusCode, null);
        return (await res.Content.ReadAsByteArrayAsync(ct),
            res.Content.Headers.ContentType?.MediaType ?? info["mime_type"]?.GetValue<string>() ?? "application/octet-stream");
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(O.AccessToken) || string.IsNullOrWhiteSpace(O.PhoneNumberId))
            throw new WhatsAppApiException("WhatsApp is not configured (AccessToken and PhoneNumberId are required).", 503, null);

        var url = path.StartsWith("https://", StringComparison.Ordinal) ? path : Base + path;
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.AccessToken);
        req.Content = body switch
        {
            null => null,
            HttpContent content => content,
            JsonNode node => new StringContent(node.ToJsonString(), System.Text.Encoding.UTF8, "application/json"),
            _ => JsonContent.Create(body),
        };

        HttpResponseMessage res;
        try { res = await http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new WhatsAppApiException($"Could not reach Meta: {ex.Message}", 0, null); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new WhatsAppApiException("Meta did not respond in time.", 0, null); }

        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonNode? json;
            try { json = JsonNode.Parse(text); }
            catch (JsonException) { json = null; }

            if (!res.IsSuccessStatusCode || json is null)
            {
                var err = json?["error"];
                var message = err?["error_user_msg"]?.GetValue<string>()
                    ?? err?["error_data"]?["details"]?.GetValue<string>()
                    ?? err?["message"]?.GetValue<string>()
                    ?? $"Meta returned HTTP {(int)res.StatusCode}.";
                var code = err?["code"]?.GetValue<int>();
                var prefixed = code is null || message.Contains($"#{code}") ? message : $"(#{code}) {message}";
                throw new WhatsAppApiException(prefixed, (int)res.StatusCode, code);
            }
            return json;
        }
    }
}
