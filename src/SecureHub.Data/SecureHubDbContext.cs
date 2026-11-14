using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SecureHub.Core.Domain;

namespace SecureHub.Data;

/// <summary>
/// SQLite 把 DateTimeOffset 存为 TEXT 时无法做范围比较与排序，
/// 因此统一转成 Unix 毫秒整数；Postgres 保持原生 timestamptz。
/// </summary>
public sealed class DateTimeOffsetToUnixMillisecondsConverter()
    : ValueConverter<DateTimeOffset, long>(
        value => value.ToUnixTimeMilliseconds(),
        value => DateTimeOffset.FromUnixTimeMilliseconds(value));

public class SecureHubDbContext : DbContext
{
    public SecureHubDbContext(DbContextOptions options) : base(options)
    {
    }
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<App> Apps => Set<App>();
    public DbSet<User> Users => Set<User>();
    public DbSet<VirtualKey> VirtualKeys => Set<VirtualKey>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<ProviderCredential> ProviderCredentials => Set<ProviderCredential>();
    public DbSet<ProviderOffering> ProviderOfferings => Set<ProviderOffering>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();
    public DbSet<ModelGroup> ModelGroups => Set<ModelGroup>();
    public DbSet<ModelGroupMember> ModelGroupMembers => Set<ModelGroupMember>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuditAnchor> AuditAnchors => Set<AuditAnchor>();
    public DbSet<AuditEventDispute> AuditEventDisputes => Set<AuditEventDispute>();
    public DbSet<ContentBlob> ContentBlobs => Set<ContentBlob>();
    public DbSet<ContentDek> ContentDeks => Set<ContentDek>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<SecurityPolicy> SecurityPolicies => Set<SecurityPolicy>();
    public DbSet<SecurityPolicyVersion> SecurityPolicyVersions => Set<SecurityPolicyVersion>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<CatalogModel> CatalogModels => Set<CatalogModel>();
    public DbSet<CatalogProvider> CatalogProviders => Set<CatalogProvider>();
    public DbSet<AssistantSession> AssistantSessions => Set<AssistantSession>();
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();
    public DbSet<RedactionToken> RedactionTokens => Set<RedactionToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite 不支持 DateTimeOffset 的比较/排序；转换为整数后索引与范围查询都可用。
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>()
                .HaveConversion<DateTimeOffsetToUnixMillisecondsConverter>();
        }
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Team>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<App>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasIndex(x => x.TeamId);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.UserName).IsUnique();
            e.Property(x => x.PasswordHash).IsRequired();
            e.Ignore(x => x.Roles);
        });

        b.Entity<VirtualKey>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.KeyId).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.KeyId).IsUnique();
            e.Property(x => x.Prefix).HasMaxLength(32).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.SecretHash).IsRequired();
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });

        b.Entity<Provider>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Alias).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Alias).IsUnique();
            e.Property(x => x.ProviderKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.Scope, x.OwnerUserId });
        });

        b.Entity<ProviderCredential>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CipherText).IsRequired();
            e.Property(x => x.KeyHint).HasMaxLength(16);
            e.HasIndex(x => x.OwnerUserId);
            e.HasIndex(x => x.ProviderId);
        });

        b.Entity<ProviderOffering>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UpstreamModel).HasMaxLength(200).IsRequired();
            e.Property(x => x.GroupKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.InputPricePerMillion).HasPrecision(18, 6);
            e.Property(x => x.OutputPricePerMillion).HasPrecision(18, 6);
            e.HasIndex(x => new { x.ProviderId, x.UpstreamModel }).IsUnique();
            e.HasIndex(x => x.GroupKey);
        });

        b.Entity<Entitlement>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.CostQuota).HasPrecision(18, 6);
            e.HasIndex(x => new { x.SubjectType, x.SubjectId });
            e.HasIndex(x => x.ProviderId);
        });

        b.Entity<ModelGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ModelGroupMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.UpstreamModel).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.GroupId, x.Priority });
        });

        b.Entity<AuditEvent>(e =>
        {
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventType).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorType).HasMaxLength(32).IsRequired();
            e.Property(x => x.ActorId).HasMaxLength(100).IsRequired();
            e.Property(x => x.ActorLabel).HasMaxLength(200).IsRequired();
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
            e.Property(x => x.PrevHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Hash).HasMaxLength(64).IsRequired();

            e.HasIndex(x => x.Sequence).IsUnique();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.AppId, x.OccurredAt });
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => new { x.PolicyId, x.RuleId });
            e.HasIndex(x => x.BillingOwner);
        });

        b.Entity<AuditAnchor>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Hash).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.Sequence);
        });

        b.Entity<AuditEventDispute>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.DisputeType).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.EventId);
        });

        b.Entity<ContentBlob>(e =>
        {
            e.HasKey(x => x.ContentId);
            e.Property(x => x.RequestId).HasMaxLength(64).IsRequired();
            e.Property(x => x.DekId).HasMaxLength(32).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.CipherText).IsRequired();
            e.HasIndex(x => new { x.RequestId, x.Direction, x.Variant }).IsUnique();
            e.HasIndex(x => new { x.AppId, x.ExpiresAt });
            e.HasIndex(x => x.ExpiresAt);
        });

        b.Entity<ContentDek>(e =>
        {
            e.HasKey(x => x.DekId);
            e.Property(x => x.DekId).HasMaxLength(32);
            e.Property(x => x.Algorithm).HasMaxLength(32).IsRequired();
            e.Property(x => x.WrappedDek).IsRequired();
        });

        b.Entity<UsageEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Model).HasMaxLength(200).IsRequired();
            e.Property(x => x.UpstreamModel).HasMaxLength(200);
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.AppId, x.OccurredAt });
            e.HasIndex(x => x.BillingOwner);
            e.HasIndex(x => x.ProviderId);
        });

        b.Entity<SecurityPolicy>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PolicyKey).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.PolicyKey).IsUnique();
        });

        b.Entity<SecurityPolicyVersion>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OnError).HasMaxLength(16).IsRequired();
            e.HasIndex(x => new { x.PolicyId, x.Version }).IsUnique();
        });

        b.Entity<Budget>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Period).HasMaxLength(16).IsRequired();
            e.Property(x => x.CostLimit).HasPrecision(18, 6);
            e.Property(x => x.CostUsed).HasPrecision(18, 6);
            e.HasIndex(x => new { x.Subject, x.SubjectId });
        });

        b.Entity<CatalogModel>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(200);
            e.Property(x => x.ProviderKey).HasMaxLength(100).IsRequired();
            e.Property(x => x.ModelId).HasMaxLength(200).IsRequired();
            e.Property(x => x.InputPricePerMillion).HasPrecision(18, 6);
            e.Property(x => x.OutputPricePerMillion).HasPrecision(18, 6);
            e.HasIndex(x => x.ProviderKey);
        });

        b.Entity<CatalogProvider>(e =>
        {
            e.HasKey(x => x.ProviderKey);
            e.Property(x => x.ProviderKey).HasMaxLength(100);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Api).HasMaxLength(500);
            e.Property(x => x.Doc).HasMaxLength(500);
            e.Property(x => x.Npm).HasMaxLength(200);
        });

        b.Entity<AssistantSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.ModelAlias).HasMaxLength(200);
            // 会话只属于本人；用户端查询永远带 UserId 过滤。
            e.HasIndex(x => new { x.UserId, x.UpdatedAt });
        });

        b.Entity<AssistantMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.ToolNames).HasMaxLength(500);
            e.Property(x => x.Model).HasMaxLength(200);
            e.HasIndex(x => new { x.SessionId, x.CreatedAt });
            e.HasOne<AssistantSession>()
                .WithMany()
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RedactionToken>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenId).HasMaxLength(64).IsRequired();
            e.Property(x => x.RequestId).HasMaxLength(64).IsRequired();
            e.Property(x => x.DetectorId).HasMaxLength(64).IsRequired();
            e.Property(x => x.DekId).HasMaxLength(32).IsRequired();
            e.Property(x => x.CipherText).IsRequired();
            e.HasIndex(x => x.TokenId).IsUnique();
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => x.ExpiresAt);
        });
    }

    public override int SaveChanges()
    {
        GuardAuditImmutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GuardAuditImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>审计表只允许 INSERT（PRD §7.5.3 / 不变量 I2）。</summary>
    private void GuardAuditImmutability()
    {
        foreach (var entry in ChangeTracker.Entries<AuditEvent>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "AuditEvent is append-only: updates and deletes are forbidden.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AuditAnchor>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("AuditAnchor is append-only.");
            }
        }
    }
}
