using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WaPlatform.Api.Domain;

namespace WaPlatform.Api.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();

    // Cookie-signing keys, shared across app restarts and recycles (in-memory keys would log everyone out).
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Name).HasMaxLength(200);
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.PasswordHash).HasMaxLength(200);
            e.Property(u => u.Role).HasMaxLength(20);
            e.Property(u => u.SecurityStamp).HasMaxLength(64);
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("audit_log");
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.Action);
            e.Property(a => a.UserEmail).HasMaxLength(256);
            e.Property(a => a.Action).HasMaxLength(64);
            e.Property(a => a.Target).HasMaxLength(256);
            e.Property(a => a.Ip).HasMaxLength(64);
        });

        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        b.Entity<WebhookEvent>(e =>
        {
            e.ToTable("webhook_events");
            e.HasIndex(w => w.ProcessedAt);
        });

        // snake_case columns to match the plan's schema (password_hash, is_active, ...)
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(SnakeCase().Replace(property.Name, "$1_$2").ToLowerInvariant());
    }

    // All timestamps are stored as UTC; mark them as such when read back so the API emits "...Z".
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<DateTime>().HaveConversion<UtcConverter>();
        c.Properties<DateTime?>().HaveConversion<UtcConverter>();
    }

    private class UtcConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex SnakeCase();
}
