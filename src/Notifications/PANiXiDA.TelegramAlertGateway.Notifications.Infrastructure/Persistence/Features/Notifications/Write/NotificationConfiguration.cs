using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.Enumerations;
using PANiXiDA.TelegramAlertGateway.Notifications.Domain.Notifications.ValueObjects;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Features.Notifications.Write;

internal sealed class NotificationConfiguration : AuditableEntityConfiguration<Notification>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .HasConversion(
                id => id.Value,
                value => NotificationId.Create(value).Value)
            .ValueGeneratedNever();

        builder.Property(item => item.Key)
            .HasConversion(
                key => key.Value,
                value => NotificationKey.Create(value).Value)
            .HasMaxLength(NotificationKey.MaxLength)
            .IsRequired();

        builder.HasIndex(item => item.Key)
            .IsUnique();

        builder.Property(item => item.Topic)
            .HasConversion(
                topic => topic.Value,
                value => TopicName.Create(value).Value)
            .HasMaxLength(TopicName.MaxLength)
            .IsRequired();

        builder.Property(item => item.Kind)
            .HasConversion(
                kind => kind.Name,
                value => NotificationKind.FromName(value))
            .HasMaxLength(NotificationKind.MaxLength)
            .IsRequired();

        builder.Property(item => item.Message)
            .HasConversion(
                message => message.Value,
                value => NotificationMessage.Create(value).Value)
            .HasMaxLength(NotificationMessage.MaxLength)
            .IsRequired();

        builder.OwnsOne(item => item.Delivery, delivery =>
        {
            delivery.Property(value => value.Status)
                .HasConversion(
                    status => status.Name,
                    value => NotificationStatus.FromName(value))
                .HasMaxLength(NotificationStatus.MaxLength)
                .IsRequired();

            delivery.Property(value => value.Attempts)
                .IsRequired();

            delivery.Property(value => value.CreatedAtUtc)
                .IsRequired();

            delivery.Property(value => value.AvailableAtUtc)
                .IsRequired();

            delivery.Property(value => value.SentAtUtc);

            delivery.Property(value => value.LastError)
                .HasMaxLength(NotificationDelivery.MaxLength);

            delivery.HasIndex(value => new
            {
                value.Status,
                value.AvailableAtUtc
            });
            delivery.HasIndex(value => new
            {
                value.Status,
                value.SentAtUtc
            });
        });

        builder.Navigation(item => item.Delivery)
            .IsRequired();
    }
}
