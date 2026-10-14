using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Domain;
using WaPlatform.Api.Services;

namespace WaPlatform.Api.Controllers;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);
public record MeDto(int Id, string Name, string Email, string Role, bool MustChangePassword);
public record ApiError(string Error);

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, AuditService audit, TokenService tokens, IOptions<AuthOptions> authOptions) : ControllerBase
{
    private const string InvalidCredentials = "Invalid email or password.";

    [AllowAnonymous]
    [EnableRateLimiting(AuthSetup.LoginRateLimit)]
    [HttpPost("login")]
    public async Task<ActionResult<MeDto>> Login(LoginRequest req)
    {
        var opts = authOptions.Value;
        var now = DateTime.UtcNow;
        var email = AuthSetup.NormalizeEmail(req.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);

        if (user is not null && user.IsLockedOut(now))
        {
            audit.Record(AuditActions.LoginLocked, email, actor: user);
            await db.SaveChangesAsync();
            return StatusCode(StatusCodes.Status423Locked,
                new ApiError("Too many failed attempts. The account is locked; try again later or ask an admin to unlock it."));
        }

        var passwordOk = Passwords.Verify(req.Password, user?.PasswordHash);

        if (user is null || !passwordOk || !user.IsActive)
        {
            if (user is not null && !passwordOk)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= opts.MaxFailedLogins)
                {
                    user.LockoutUntil = now.AddMinutes(opts.LockoutMinutes);
                    user.FailedLoginCount = 0;
                }
            }
            audit.Record(AuditActions.LoginFailed, email,
                new { reason = user is null ? "unknown_email" : !passwordOk ? "bad_password" : "disabled" }, user);
            await db.SaveChangesAsync();
            return Unauthorized(new ApiError(InvalidCredentials));
        }

        user.FailedLoginCount = 0;
        user.LockoutUntil = null;
        user.LastLoginAt = now;
        audit.Record(AuditActions.Login, actor: user);
        await db.SaveChangesAsync();

        await tokens.IssueAsync(user);
        return ToMe(user);
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token and refresh token. The frontend calls
    /// this when a request answers 401 because the access token (15 minutes) ran out.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<MeDto>> Refresh()
    {
        var (outcome, user) = await tokens.RefreshAsync();
        if (outcome == RefreshOutcome.Rejected)
        {
            tokens.ClearCookies();
            return Unauthorized(new ApiError("Your session has ended. Please sign in again."));
        }
        return ToMe(user!);
    }

    /// <summary>Works signed in or not, so a browser whose access token expired can still sign out.</summary>
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            audit.Record(AuditActions.Logout);
            await db.SaveChangesAsync();
        }
        await tokens.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me()
    {
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == User.GetUserId());
        return ToMe(user);
    }

    /// <summary>Any signed-in user may change their own password (and only their own).</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.GetUserId());

        if (!Passwords.Verify(req.CurrentPassword, user.PasswordHash))
            return BadRequest(new ApiError("Current password is incorrect."));
        if (Passwords.Validate(req.NewPassword) is { } problem)
            return BadRequest(new ApiError(problem));
        if (req.NewPassword == req.CurrentPassword)
            return BadRequest(new ApiError("New password must be different from the current one."));

        user.PasswordHash = Passwords.Hash(req.NewPassword);
        user.MustChangePassword = false;
        user.RotateStamp(); // ends the user's other sessions
        audit.Record(AuditActions.PasswordChanged, actor: user);
        await db.SaveChangesAsync();

        // The new stamp ends every session; start a fresh one so this browser stays signed in.
        await tokens.RevokeCurrentAsync();
        await tokens.IssueAsync(user);
        return NoContent();
    }

    private static MeDto ToMe(User u) => new(u.Id, u.Name, u.Email, u.Role, u.MustChangePassword);
}
