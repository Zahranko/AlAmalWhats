using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record UserDto(int Id, string Name, string Email, string Role, bool IsActive, bool IsLocked,
    bool MustChangePassword, DateTime? LastLoginAt, DateTime CreatedAt);

public record CreateUserRequest(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required] string Role,
    [Required] string Password);

public record UpdateUserRequest(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required] string Role,
    bool IsActive);

public record ResetPasswordRequest([Required] string Password);

/// <summary>User management. Admin only: employees get 403 even when calling the API directly.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Admin)]
public class UsersController(AppDbContext db, AuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> List()
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync();
        var now = DateTime.UtcNow;
        return users.Select(u => ToDto(u, now)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest req)
    {
        if (!Roles.IsValid(req.Role)) return BadRequest(new ApiError("Role must be admin or employee."));
        if (Passwords.Validate(req.Password) is { } problem) return BadRequest(new ApiError(problem));

        var email = AuthSetup.NormalizeEmail(req.Email);
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new ApiError("A user with this email already exists."));

        var user = new User
        {
            Name = req.Name.Trim(),
            Email = email,
            Role = req.Role,
            PasswordHash = Passwords.Hash(req.Password),
            MustChangePassword = true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        audit.Record(AuditActions.UserCreated, UserTarget(user), new { user.Name, user.Email, user.Role });
        await db.SaveChangesAsync();
        return ToDto(user, DateTime.UtcNow);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest req)
    {
        if (!Roles.IsValid(req.Role)) return BadRequest(new ApiError("Role must be admin or employee."));
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        var isSelf = id == User.GetUserId();
        if (isSelf && (req.Role != user.Role || !req.IsActive))
            return BadRequest(new ApiError("You cannot change your own role or disable yourself."));

        var losesAdmin = user.Role == Roles.Admin && user.IsActive && (req.Role != Roles.Admin || !req.IsActive);
        if (losesAdmin && !await OtherActiveAdminExists(id))
            return BadRequest(new ApiError("There must be at least one active admin."));

        var email = AuthSetup.NormalizeEmail(req.Email);
        if (email != user.Email && await db.Users.AnyAsync(u => u.Email == email && u.Id != id))
            return Conflict(new ApiError("A user with this email already exists."));

        var before = new { user.Name, user.Email, user.Role, user.IsActive };
        var endSessions = user.Role != req.Role || user.IsActive != req.IsActive || user.Email != email;

        user.Name = req.Name.Trim();
        user.Email = email;
        user.Role = req.Role;
        user.IsActive = req.IsActive;
        user.UpdatedAt = DateTime.UtcNow;
        if (endSessions) user.RotateStamp(); // disabled / re-roled users are logged out at once

        audit.Record(AuditActions.UserUpdated, UserTarget(user),
            new { before, after = new { user.Name, user.Email, user.Role, user.IsActive } });
        await db.SaveChangesAsync();
        return ToDto(user, DateTime.UtcNow);
    }

    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req)
    {
        if (Passwords.Validate(req.Password) is { } problem) return BadRequest(new ApiError(problem));
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.PasswordHash = Passwords.Hash(req.Password);
        user.MustChangePassword = true;
        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.RotateStamp();

        audit.Record(AuditActions.UserPasswordReset, UserTarget(user));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/unlock")]
    public async Task<IActionResult> Unlock(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.UpdatedAt = DateTime.UtcNow;

        audit.Record(AuditActions.UserUnlocked, UserTarget(user));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id == User.GetUserId()) return BadRequest(new ApiError("You cannot delete yourself."));
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        if (user.Role == Roles.Admin && user.IsActive && !await OtherActiveAdminExists(id))
            return BadRequest(new ApiError("There must be at least one active admin."));

        // Keep "sent by" in message history intact: users who have sent messages are disabled, not deleted.
        if (await db.Messages.AnyAsync(m => m.SentById == id) || await db.Campaigns.AnyAsync(c => c.CreatedById == id))
            return BadRequest(new ApiError("This user has sent messages. Disable the user instead, so the history keeps their name."));

        db.Users.Remove(user);
        audit.Record(AuditActions.UserDeleted, UserTarget(user), new { user.Name, user.Email, user.Role });
        await db.SaveChangesAsync();
        return NoContent();
    }

    private Task<bool> OtherActiveAdminExists(int exceptId) =>
        db.Users.AnyAsync(u => u.Id != exceptId && u.Role == Roles.Admin && u.IsActive);

    private static string UserTarget(User u) => $"user:{u.Id} {u.Email}";

    private static UserDto ToDto(User u, DateTime now) =>
        new(u.Id, u.Name, u.Email, u.Role, u.IsActive, u.IsLockedOut(now), u.MustChangePassword, u.LastLoginAt, u.CreatedAt);
}
