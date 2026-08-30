using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Notifications;

internal sealed class NotificationConfiguration : AuditableEntityConfiguration<Notification>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasConversion(NotificationIdConverter)
            .ValueGeneratedNever();

        builder.Property(item => item.Key)
            .HasConversion(NotificationKeyConverter)
            .HasMaxLength(NotificationKey.Length)
            .IsRequired();

        builder.HasIndex(item => item.Key)
            .IsUnique();

        builder.Property(item => item.Topic)
            .HasConversion(TopicNameConverter)
            .HasMaxLength(TopicName.MaxLength)
            .IsRequired();

        builder.Property(item => item.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(item => item.Message)
            .HasMaxLength(Notification.MaxMessageLength)
            .IsRequired();

        builder.Property(item => item.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(item => item.LastError)
            .HasMaxLength(Notification.MaxErrorLength);

        builder.HasIndex(item => new { item.Status, item.AvailableAtUtc });
    }

    private static readonly ValueConverter<NotificationId, Guid> NotificationIdConverter = new(
        id => id.Value,
        value => NotificationId.Create(value).Value);

    private static readonly ValueConverter<NotificationKey, string> NotificationKeyConverter = new(
        key => key.Value,
        value => NotificationKey.Create(value).Value);

    private static readonly ValueConverter<TopicName, string> TopicNameConverter = new(
        topic => topic.Value,
        value => TopicName.Create(value).Value);
}
