using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;
using Scheduler.Infrastructure.Persistence;

namespace Scheduler.Infrastructure.Persistence.Configurations;

/// <summary>configuration table (Phase 4, Table 5.9).</summary>
internal sealed class ConfigurationSettingConfiguration : IEntityTypeConfiguration<ConfigurationSetting>
{
    public void Configure(EntityTypeBuilder<ConfigurationSetting> b)
    {
        b.ToTable("configuration", t =>
            t.HasCheckConstraint("ck_configuration_scope_task", "(scope = 'Global') = (task_id IS NULL)"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("config_id").ValueGeneratedNever();
        b.Property(x => x.TaskId).HasColumnName("task_id");
        b.Property(x => x.Key).HasColumnName("config_key").HasMaxLength(ConfigurationSetting.KeyMaxLength).IsRequired();
        b.Property(x => x.Value).HasColumnName("config_value").HasMaxLength(ConfigurationSetting.ValueMaxLength).IsRequired();
        b.Property(x => x.Scope).IsEnumText(b, "scope", 10);
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at").HasDefaultValueSql("now()");
        b.Property(x => x.Version).IsRowVersion();
        b.HasOne<ScheduledTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_configuration_task");
        // NULLS NOT DISTINCT: a global key (task_id NULL) can also exist only once (BR-10).
        b.HasIndex(x => new { x.TaskId, x.Key }).IsUnique().AreNullsDistinct(false).HasDatabaseName("ux_configuration_task_key");
    }
}

/// <summary>logging_provider table (Phase 4, Table 5.11).</summary>
internal sealed class LoggingProviderConfiguration : IEntityTypeConfiguration<LoggingProviderSetting>
{
    public void Configure(EntityTypeBuilder<LoggingProviderSetting> b)
    {
        b.ToTable("logging_provider");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("provider_id").ValueGeneratedNever();
        b.Property(x => x.ProviderName).HasColumnName("provider_name").HasMaxLength(LoggingProviderSetting.NameMaxLength).IsRequired();
        b.Property(x => x.ProviderType).IsEnumText(b, "provider_type", 20);
        b.Property(x => x.MinimumLevel).IsEnumText(b, "minimum_level", 10);
        b.Property(x => x.Settings).HasColumnName("settings").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
        b.Property(x => x.Version).IsRowVersion();
        b.HasIndex(x => x.ProviderName).IsUnique().HasDatabaseName("ux_logging_provider_name");
    }
}

/// <summary>app_user table (Phase 4, Table 5.12).</summary>
internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("app_user");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("user_id").ValueGeneratedNever();
        b.Property(x => x.Username).HasColumnName("username").HasMaxLength(AppUser.UsernameMaxLength).IsRequired();
        b.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordSalt).HasColumnName("password_salt").HasMaxLength(64).IsRequired();
        b.Property(x => x.Role).IsEnumText(b, "role", 20);
        b.Property(x => x.FailedLoginCount).HasColumnName("failed_login_count").HasDefaultValue(0);
        b.Property(x => x.LockoutEndUtc).HasColumnName("lockout_end");
        b.Property(x => x.LastLoginAtUtc).HasColumnName("last_login_at");
        b.Property(x => x.RecoveryCodeHash).HasColumnName("recovery_code_hash").HasMaxLength(256);
        b.Property(x => x.RecoveryCodeSalt).HasColumnName("recovery_code_salt").HasMaxLength(64);
        b.Property(x => x.Version).IsRowVersion();
        b.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).HasSentinel(true);
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at");
        // ux_user_username (lower(username)) is created in the migration.
    }
}

/// <summary>login_audit table (Phase 4, Table 5.13).</summary>
internal sealed class LoginAuditConfiguration : IEntityTypeConfiguration<LoginAudit>
{
    public void Configure(EntityTypeBuilder<LoginAudit> b)
    {
        b.ToTable("login_audit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("audit_id").ValueGeneratedNever();
        b.Property(x => x.UserId).HasColumnName("user_id");
        b.Property(x => x.UsernameAttempted).HasColumnName("username_attempted").HasMaxLength(AppUser.UsernameMaxLength).IsRequired();
        b.Property(x => x.Succeeded).HasColumnName("succeeded");
        b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(LoginAudit.ReasonMaxLength);
        b.Property(x => x.AttemptedAtUtc).HasColumnName("attempted_at").HasDefaultValueSql("now()");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull).HasConstraintName("fk_login_audit_user");
        b.HasIndex(x => x.AttemptedAtUtc).HasDatabaseName("ix_login_audit_time");
    }
}

/// <summary>engine_instance table: at most one row, the engine that holds the single-engine lock (WP-1).</summary>
internal sealed class EngineInstanceConfiguration : IEntityTypeConfiguration<EngineInstance>
{
    public void Configure(EntityTypeBuilder<EngineInstance> b)
    {
        b.ToTable("engine_instance", t => t.HasCheckConstraint("ck_engine_instance_single_row", "instance_id = 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("instance_id").ValueGeneratedNever();
        b.Property(x => x.HostName).HasColumnName("host_name").HasMaxLength(WorkerThread.HostNameMaxLength).IsRequired();
        b.Property(x => x.ProcessId).HasColumnName("process_id");
        b.Property(x => x.StartedAtUtc).HasColumnName("started_at");
        b.Property(x => x.HeartbeatAtUtc).HasColumnName("heartbeat_at");
    }
}
