using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.VictoriaLogs;

internal sealed class LogIngestionCheckpointConfiguration
    : IEntityTypeConfiguration<LogIngestionCheckpoint>
{
    public void Configure(EntityTypeBuilder<LogIngestionCheckpoint> builder)
    {
        builder.ToTable("log_ingestion_checkpoints");
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).HasMaxLength(64);
    }
}
