using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace WaPlatform.Api.WhatsApp;

public class WhatsAppApiException(string message, int statusCode, int? metaCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public int? MetaCode { get; } = metaCode;
}

public record PhoneNumberInfo(string Id, string? DisplayPhoneNumber, string? VerifiedName, string? QualityRating);

/// <summary>
/// Thin client for the Graph API. Options are read per call (IOptionsMonitor), so a rotated
/// token in configuration takes effect without a restart.
/// </summary>
public class WhatsAppClient(HttpClient http, IOptionsMonitor<WhatsAppOptions> options)
{
    public async Task<PhoneNumberInfo> GetPhoneNumberAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var json = await SendAsync(HttpMethod.Get,
            $"{o.PhoneNumberId}?fields=display_phone_number,verified_name,quality_rating", null, ct);
        return new PhoneNumberInfo(
            json["id"]?.GetValue<string>() ?? o.PhoneNumberId!,
            json["display_phone_number"]?.GetValue<string>(),
            json["verified_name"]?.GetValue<string>(),
            json["quality_rating"]?.GetValue<string>());
    }

    /// <summary>Sends an approved template with no parameters; returns Meta's message id (wamid).</summary>
    public async Task<string> SendTemplateAsync(string to, string templateName, string languageCode, CancellationToken ct = default)
    {
        var body = new
        {
            messaging_product = "whatsapp",
            to,
            type = "template",
            template = new { name = templateName, language = new { code = languageCode } },
        };
        var json = await SendAsync(HttpMethod.Post, $"{options.CurrentValue.PhoneNumberId}/messages", body, ct);
        return json["messages"]?[0]?["id"]?.GetValue<string>()
            ?? throw new WhatsAppApiException("Meta accepted the request but returned no message id.", 502, null);
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var o = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.AccessToken) || string.IsNullOrWhiteSpace(o.PhoneNumberId))
            throw new WhatsAppApiException("WhatsApp is not configured (AccessToken and PhoneNumberId are required).", 503, null);

        using var req = new HttpRequestMessage(method, $"https://graph.facebook.com/{o.GraphApiVersion}/{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
        if (body is not null) req.Content = JsonContent.Create(body);

        using var res = await http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? json;
        try { json = JsonNode.Parse(text); }
        catch (JsonException) { json = null; }

        if (!res.IsSuccessStatusCode || json is null)
        {
            var err = json?["error"];
            var message = err?["error_user_msg"]?.GetValue<string>() ?? err?["message"]?.GetValue<string>()
                ?? $"Meta returned HTTP {(int)res.StatusCode}.";
            throw new WhatsAppApiException(message, (int)res.StatusCode, err?["code"]?.GetValue<int>());
        }
        return json;
    }
}
