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
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

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
            e.Property(w => w.Error).HasMaxLength(2000);
        });

        b.Entity<Template>(e =>
        {
            e.ToTable("templates");
            e.HasIndex(t => t.MetaId).IsUnique();
            e.HasIndex(t => new { t.Name, t.Language });
            e.Property(t => t.MetaId).HasMaxLength(64);
            e.Property(t => t.Name).HasMaxLength(512);
            e.Property(t => t.Language).HasMaxLength(16);
            e.Property(t => t.Category).HasMaxLength(32);
            e.Property(t => t.Status).HasMaxLength(32);
            e.Property(t => t.ParameterFormat).HasMaxLength(16);
        });

        b.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasIndex(c => c.Phone).IsUnique();
            e.HasIndex(c => c.Name);
            e.HasIndex(c => c.FileNumber);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Phone).HasMaxLength(20);
            e.Property(c => c.FileNumber).HasMaxLength(50);
            e.Property(c => c.Notes).HasMaxLength(2000);
        });

        b.Entity<MediaFile>(e =>
        {
            e.ToTable("media_files");
            e.HasIndex(m => m.CreatedAt);
            e.Property(m => m.FileName).HasMaxLength(255);
            e.Property(m => m.ContentType).HasMaxLength(100);
            e.Property(m => m.MetaMediaId).HasMaxLength(64);
        });

        b.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasIndex(m => new { m.Status, m.NextAttemptAt });
            e.HasIndex(m => new { m.CustomerId, m.CreatedAt });
            e.HasIndex(m => m.CampaignId);
            e.HasIndex(m => m.CreatedAt);
            e.HasIndex(m => m.WaMessageId).IsUnique().HasFilter("[wa_message_id] IS NOT NULL");
            e.Property(m => m.Direction).HasMaxLength(8);
            e.Property(m => m.Phone).HasMaxLength(20);
            e.Property(m => m.Type).HasMaxLength(20);
            e.Property(m => m.TemplateName).HasMaxLength(512);
            e.Property(m => m.Language).HasMaxLength(16);
            e.Property(m => m.InboundMediaId).HasMaxLength(64);
            e.Property(m => m.MediaFileName).HasMaxLength(255);
            e.Property(m => m.MediaContentType).HasMaxLength(100);
            e.Property(m => m.Status).HasMaxLength(16);
            e.Property(m => m.Error).HasMaxLength(1000);
            e.Property(m => m.WaMessageId).HasMaxLength(128);
            e.Property(m => m.Source).HasMaxLength(16);
            e.Property(m => m.PricingCategory).HasMaxLength(32);
            // History survives deleting a customer, template, file or user: the link is just cleared.
            e.HasOne(m => m.Customer).WithMany().HasForeignKey(m => m.CustomerId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.Template).WithMany().HasForeignKey(m => m.TemplateId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.MediaFile).WithMany().HasForeignKey(m => m.MediaFileId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.Campaign).WithMany().HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(m => m.SentBy).WithMany().HasForeignKey(m => m.SentById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.HasIndex(c => c.CreatedAt);
            e.Property(c => c.Name).HasMaxLength(200);
            e.Property(c => c.Status).HasMaxLength(16);
            e.HasOne(c => c.Template).WithMany().HasForeignKey(c => c.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.CreatedBy).WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasIndex(k => k.KeyHash).IsUnique();
            e.Property(k => k.Name).HasMaxLength(200);
            e.Property(k => k.Prefix).HasMaxLength(16);
            e.Property(k => k.KeyHash).HasMaxLength(64);
        });

        b.Entity<AppSetting>(e =>
        {
            e.ToTable("settings");
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(100);
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
