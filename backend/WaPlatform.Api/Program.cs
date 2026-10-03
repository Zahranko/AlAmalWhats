using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api;
using WaPlatform.Api.Auth;
using WaPlatform.Api.Data;
using WaPlatform.Api.Services;
using WaPlatform.Api.WhatsApp;

var isCli = Cli.IsCommand(args);
var builder = WebApplication.CreateBuilder(isCli ? [] : args);

// Local secrets (git-ignored). Environment variables are re-added after it so they still win in production.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddDataProtection().SetApplicationName("WaPlatform").PersistKeysToDbContext<AppDbContext>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddAppAuth(builder.Configuration, builder.Environment);
builder.Services.AddControllers();
builder.Services.Configure<WhatsAppOptions>(builder.Configuration.GetSection("WhatsApp"));
builder.Services.AddHttpClient<WhatsAppClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
if (!isCli) builder.Services.AddHostedService<RetentionService>();

// The browser normally reaches the API through the Next.js server (same origin), but allow the
// frontend's origin directly as well, with cookies.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

if (isCli)
    return await Cli.RunAsync(app.Services, args);

// The app runs behind the Next.js server / a reverse proxy; trust its X-Forwarded-* headers
// (loopback proxies only, by default) so audit logs record the real client IP.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseTrustedProxyClientIp(builder.Configuration["Proxy:Secret"]);

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

var missingWhatsApp = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<WhatsAppOptions>>().Value
    .MissingSettings().ToList();
if (missingWhatsApp.Count > 0)
    app.Logger.LogWarning("WhatsApp settings missing: {Missing}. Sending and/or webhooks will not work.",
        string.Join(", ", missingWhatsApp));

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseRequirePasswordChange();
app.UseAuthorization();

app.MapControllers();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Public pages required by Meta for the app (Privacy Policy URL, Terms of Service URL).
foreach (var page in new[] { "privacy", "terms" })
{
    var path = Path.Combine(app.Environment.ContentRootPath, "Legal", $"{page}.html");
    app.MapGet($"/{page}", () => Results.File(path, "text/html; charset=utf-8")).AllowAnonymous();
}

await app.RunAsync();
return 0;
