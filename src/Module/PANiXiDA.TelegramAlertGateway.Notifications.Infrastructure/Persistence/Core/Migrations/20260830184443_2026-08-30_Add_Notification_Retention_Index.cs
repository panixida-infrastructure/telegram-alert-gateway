using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class _20260830_Add_Notification_Retention_Index : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_sent_at_utc",
                table: "notifications",
                columns: new[] { "status", "sent_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_status_sent_at_utc",
                table: "notifications");
        }
    }
}
