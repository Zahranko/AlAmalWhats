using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WaPlatform.Api.WhatsApp;

public record ButtonParam(int Index, string SubType, string Text);

/// <summary>Values for a template's variables, in the order the template declares them.</summary>
public record TemplateParams(List<string>? Header = null, List<string>? Body = null, List<string>? Buttons = null)
{
    public static TemplateParams Empty => new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);

    public static TemplateParams FromJson(string? json) =>
        string.IsNullOrEmpty(json) ? Empty : JsonSerializer.Deserialize<TemplateParams>(json, JsonSerializerOptions.Web) ?? Empty;
}

/// <summary>
/// What a template needs to be sent, read from Meta's components: header type and variables,
/// body variables, and buttons that take a value (dynamic URL suffix, copy-code).
/// </summary>
public partial class TemplateSpec
{
    public string? HeaderFormat { get; private init; }
    public string? HeaderText { get; private init; }
    public List<string> HeaderParams { get; private init; } = [];
    public string? BodyText { get; private init; }
    public List<string> BodyParams { get; private init; } = [];
    public string? FooterText { get; private init; }
    public List<ButtonParam> ButtonParams { get; private init; } = [];
    /// <summary>All buttons as (type, text) for previews.</summary>
    public List<(string Type, string Text)> Buttons { get; private init; } = [];
    public bool IsNamed { get; private init; }

    public bool NeedsMedia => HeaderFormat is "IMAGE" or "VIDEO" or "DOCUMENT";

    public static TemplateSpec Parse(string componentsJson, string parameterFormat)
    {
        var components = JsonNode.Parse(componentsJson)?.AsArray() ?? [];
        string? headerFormat = null, headerText = null, bodyText = null, footerText = null;
        var buttonParams = new List<ButtonParam>();
        var buttons = new List<(string, string)>();

        foreach (var c in components)
        {
            switch (c?["type"]?.GetValue<string>())
            {
                case "HEADER":
                    headerFormat = c["format"]?.GetValue<string>();
                    headerText = c["text"]?.GetValue<string>();
                    break;
                case "BODY":
                    bodyText = c["text"]?.GetValue<string>();
                    break;
                case "FOOTER":
                    footerText = c["text"]?.GetValue<string>();
                    break;
                case "BUTTONS":
                    var list = c["buttons"]?.AsArray() ?? [];
                    for (var i = 0; i < list.Count; i++)
                    {
                        var type = list[i]?["type"]?.GetValue<string>() ?? "";
                        var text = list[i]?["text"]?.GetValue<string>() ?? "";
                        buttons.Add((type, text));
                        if (type == "URL" && (list[i]?["url"]?.GetValue<string>() ?? "").Contains("{{"))
                            buttonParams.Add(new ButtonParam(i, "url", text));
                        else if (type == "COPY_CODE")
                            buttonParams.Add(new ButtonParam(i, "copy_code", text));
                    }
                    break;
            }
        }

        return new TemplateSpec
        {
            HeaderFormat = headerFormat,
            HeaderText = headerText,
            HeaderParams = headerFormat == "TEXT" ? Placeholders(headerText) : [],
            BodyText = bodyText,
            BodyParams = Placeholders(bodyText),
            FooterText = footerText,
            ButtonParams = buttonParams,
            Buttons = buttons,
            IsNamed = parameterFormat == "NAMED",
        };
    }

    /// <summary>Checks that every variable has a value; returns a message for the user, or null.</summary>
    public string? Validate(TemplateParams p, bool hasMedia)
    {
        if (NeedsMedia && !hasMedia) return $"This template needs a {HeaderFormat!.ToLowerInvariant()} attachment.";
        if (!NeedsMedia && hasMedia) return "This template has no attachment header; remove the file.";
        if (Missing(HeaderParams, p.Header)) return "Fill in the header variables.";
        if (Missing(BodyParams, p.Body)) return $"Fill in all {BodyParams.Count} message variables.";
        if (ButtonParams.Count > 0 && Missing(ButtonParams.Select(b => b.Text).ToList(), p.Buttons))
            return "Fill in the button values.";
        return null;

        static bool Missing(List<string> names, List<string>? values) =>
            names.Count > 0 && (values is null || values.Count < names.Count || values.Take(names.Count).Any(string.IsNullOrWhiteSpace));
    }

    /// <summary>The body with variables filled in, for history and previews.</summary>
    public string Render(TemplateParams p)
    {
        var text = BodyText ?? "";
        for (var i = 0; i < BodyParams.Count; i++)
            text = text.Replace("{{" + BodyParams[i] + "}}", p.Body is { } b && i < b.Count ? b[i] : "");
        return text;
    }

    /// <summary>Meta's "components" for sending this template.</summary>
    public JsonArray BuildComponents(TemplateParams p, string? mediaId, string? fileName)
    {
        var result = new JsonArray();

        var header = new JsonArray();
        if (NeedsMedia && mediaId is not null)
        {
            var kind = HeaderFormat!.ToLowerInvariant();
            var media = new JsonObject { ["id"] = mediaId };
            if (kind == "document" && fileName is not null) media["filename"] = fileName;
            header.Add(new JsonObject { ["type"] = kind, [kind] = media });
        }
        AddText(header, HeaderParams, p.Header);
        if (header.Count > 0) result.Add(new JsonObject { ["type"] = "header", ["parameters"] = header });

        var body = new JsonArray();
        AddText(body, BodyParams, p.Body);
        if (body.Count > 0) result.Add(new JsonObject { ["type"] = "body", ["parameters"] = body });

        for (var i = 0; i < ButtonParams.Count; i++)
        {
            var b = ButtonParams[i];
            var value = p.Buttons is { } v && i < v.Count ? v[i] : "";
            JsonObject param = b.SubType == "copy_code"
                ? new JsonObject { ["type"] = "coupon_code", ["coupon_code"] = value }
                : new JsonObject { ["type"] = "text", ["text"] = value };
            result.Add(new JsonObject
            {
                ["type"] = "button",
                ["sub_type"] = b.SubType,
                ["index"] = b.Index.ToString(),
                ["parameters"] = new JsonArray(param),
            });
        }
        return result;

        void AddText(JsonArray target, List<string> names, List<string>? values)
        {
            for (var i = 0; i < names.Count; i++)
            {
                var param = new JsonObject { ["type"] = "text", ["text"] = values is { } v && i < v.Count ? v[i] : "" };
                if (IsNamed) param["parameter_name"] = names[i];
                target.Add(param);
            }
        }
    }

    /// <summary>Distinct {{1}} / {{name}} placeholders in order of first appearance.</summary>
    private static List<string> Placeholders(string? text) =>
        text is null ? [] : PlaceholderRegex().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
