using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record ApiKeyDto(int Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt, DateTime? RevokedAt);
public record CreateApiKeyRequest([Required, MaxLength(200)] string Name);
/// <summary>The full key is returned only here, once.</summary>
public record CreatedApiKey(ApiKeyDto Key, string Secret);

[ApiController]
[Route("api/api-keys")]
[Authorize(Roles = Roles.Admin)]
public class ApiKeysController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public Task<List<ApiKeyDto>> List(CancellationToken ct) =>
        db.ApiKeys.AsNoTracking().OrderByDescending(k => k.Id)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt, k.RevokedAt)).ToListAsync(ct);

    [HttpPost]
    public async Task<CreatedApiKey> Create(CreateApiKeyRequest req, CancellationToken ct)
    {
        var secret = ApiKeyHandler.NewKey();
        var key = new ApiKey { Name = req.Name.Trim(), Prefix = secret[..12], KeyHash = ApiKeyHandler.Hash(secret), CreatedById = User.GetUserId() };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);
        audit.Record(AuditActions.ApiKeyCreated, $"apikey:{key.Id} {key.Name}", new { key.Prefix });
        await db.SaveChangesAsync(ct);
        return new CreatedApiKey(new ApiKeyDto(key.Id, key.Name, key.Prefix, key.CreatedAt, null, null), secret);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken ct)
    {
        var key = await db.ApiKeys.FindAsync([id], ct);
        if (key is null) return NotFound();
        key.RevokedAt ??= DateTime.UtcNow;
        audit.Record(AuditActions.ApiKeyRevoked, $"apikey:{key.Id} {key.Name}");
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
