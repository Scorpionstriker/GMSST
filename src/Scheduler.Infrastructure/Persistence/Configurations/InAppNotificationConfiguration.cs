using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Scheduler.Domain.Entities;

namespace Scheduler.Infrastructure.Persistence.Configurations;

internal sealed class InAppNotificationConfiguration : IEntityTypeConfiguration<InAppNotification>
{
    public void Configure(EntityTypeBuilder<InAppNotification> b)
    {
        b.ToTable("in_app_notification");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("notification_id").ValueGeneratedNever();
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(InAppNotification.SourceMaxLength).IsRequired();
        b.Property(x => x.RecipientUserId).HasColumnName("recipient_user_id");
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(InAppNotification.TitleMaxLength).IsRequired();
        b.Property(x => x.Message).HasColumnName("message").HasMaxLength(InAppNotification.MessageMaxLength).IsRequired();
        b.Property(x => x.CreatedAtUtc).HasColumnName("created_at").HasDefaultValueSql("now()");
        b.Property(x => x.DeliverAtUtc).HasColumnName("deliver_at");
        b.Property(x => x.ReadAtUtc).HasColumnName("read_at");
        b.Property(x => x.CancelledAtUtc).HasColumnName("cancelled_at");
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_notification_recipient");
        b.HasIndex(x => new { x.RecipientUserId, x.DeliverAtUtc }).HasDatabaseName("ix_notification_inbox")
            .HasFilter("cancelled_at IS NULL");
        b.HasIndex(x => new { x.Source, x.DeliverAtUtc }).HasDatabaseName("ix_notification_source")
            .HasFilter("cancelled_at IS NULL");
    }
}
