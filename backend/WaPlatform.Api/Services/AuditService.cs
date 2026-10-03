using System.Security.Claims;
using System.Text.Json;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Services;

/// <summary>
/// Adds audit entries to the current DbContext; they are written by the caller's
/// SaveChangesAsync, so the audit row and the change it describes commit together.
/// </summary>
public class AuditService(AppDbContext db, IHttpContextAccessor http)
{
    public void Record(string action, string? target = null, object? details = null, User? actor = null)
    {
        var ctx = http.HttpContext;
        var principal = ctx?.User;
        var signedIn = principal?.Identity?.IsAuthenticated == true;

        db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            Target = target,
            Details = details is null ? null : JsonSerializer.Serialize(details, JsonSerializerOptions.Web),
            UserId = actor?.Id ?? (signedIn && int.TryParse(principal!.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null),
            UserEmail = actor?.Email ?? (signedIn ? principal!.FindFirstValue(ClaimTypes.Email) : null),
            Ip = ctx?.Connection.RemoteIpAddress?.ToString(),
        });
    }
}
