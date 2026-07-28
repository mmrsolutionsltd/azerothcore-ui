using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AzerothCore_UI.Api.Data;

namespace AzerothCore_UI.Api.Security;

public sealed class AdministrationActivityAudit(
    AdministrationAccountStore store,
    ILogger<AdministrationActivityAudit> logger)
{
    private const int MaximumBodyLength = 8192;
    private static readonly string[] SensitiveNames =
        ["password", "secret", "token", "apiKey", "securityStamp"];

    public async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.ContentLength is null or 0
            || request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) != true)
            return null;
        if (request.ContentLength > MaximumBodyLength)
            return $"[JSON body omitted: {request.ContentLength} bytes]";

        request.EnableBuffering();
        using var reader = new StreamReader(
            request.Body, Encoding.UTF8, false, MaximumBodyLength, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            return SanitizeJsonForAudit(body);
        }
        catch (JsonException)
        {
            return "[Unparseable JSON body omitted]";
        }
    }

    public static string SanitizeJsonForAudit(string body)
    {
        var node = JsonNode.Parse(body)
            ?? throw new JsonException("The audit JSON body was empty.");
        Redact(node);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    public async Task RecordAsync(
        HttpContext context,
        string? requestBody,
        int statusCode,
        double elapsedMilliseconds)
    {
        var path = context.Request.Path.Value ?? "/api";
        var actor = context.Request.Headers["X-AzerothCore-Actor"].ToString();
        var role = context.Request.Headers["X-AzerothCore-Role"].ToString();
        var remoteAddress =
            context.Request.Headers["X-AzerothCore-Remote-Address"].ToString();
        if (string.IsNullOrWhiteSpace(remoteAddress))
            remoteAddress = context.Connection.RemoteIpAddress?.ToString() ?? "";
        _ = ulong.TryParse(
            context.Request.Headers["X-AzerothCore-Actor-Id"], out var actorId);
        var action = $"{context.Request.Method} {path}";
        if (action.Length > 100) action = action[..100];
        var outcome = statusCode is >= 200 and < 400 ? "Succeeded" : "Failed";
        var query = context.Request.QueryString.HasValue
            ? context.Request.QueryString.Value : null;
        var detail = $"Role={ValueOrUnknown(role)}; Status={statusCode}; " +
                     $"DurationMs={elapsedMilliseconds:F1}; TraceId={context.TraceIdentifier}";
        if (!string.IsNullOrWhiteSpace(query)) detail += $"; Query={query}";
        if (!string.IsNullOrWhiteSpace(requestBody)) detail += $"; Body={requestBody}";
        if (detail.Length > 500) detail = detail[..497] + "...";
        try
        {
            await store.RecordActivityAsync(
                actorId == 0 ? null : actorId,
                ValueOrUnknown(actor),
                action,
                outcome,
                string.IsNullOrWhiteSpace(remoteAddress) ? null : remoteAddress,
                detail);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Could not persist administration activity audit for {Method} {Path}.",
                context.Request.Method, path);
        }
    }

    private static string ValueOrUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static void Redact(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            foreach (var property in value.ToArray())
            {
                if (SensitiveNames.Any(name =>
                        property.Key.Contains(name, StringComparison.OrdinalIgnoreCase)))
                    value[property.Key] = "[REDACTED]";
                else
                    Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
            foreach (var child in array) Redact(child);
    }
}
