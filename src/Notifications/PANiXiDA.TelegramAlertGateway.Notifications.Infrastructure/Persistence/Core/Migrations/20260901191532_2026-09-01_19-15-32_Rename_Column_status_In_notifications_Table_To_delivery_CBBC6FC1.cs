using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class _20260901_191532_Rename_Column_status_In_notifications_Table_To_delivery_CBBC6FC1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                sql: "TRUNCATE TABLE notifications;");

            migrationBuilder.RenameColumn(
                name: "status",
                table: "notifications",
                newName: "delivery_status");

            migrationBuilder.RenameColumn(
                name: "sent_at_utc",
                table: "notifications",
                newName: "delivery_sent_at_utc");

            migrationBuilder.RenameColumn(
                name: "last_error",
                table: "notifications",
                newName: "delivery_last_error");

            migrationBuilder.RenameColumn(
                name: "created_at_utc",
                table: "notifications",
                newName: "delivery_created_at_utc");

            migrationBuilder.RenameColumn(
                name: "available_at_utc",
                table: "notifications",
                newName: "delivery_available_at_utc");

            migrationBuilder.RenameColumn(
                name: "attempts",
                table: "notifications",
                newName: "delivery_attempts");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_status_sent_at_utc",
                table: "notifications",
                newName: "ix_notifications_delivery_status_delivery_sent_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_status_available_at_utc",
                table: "notifications",
                newName: "ix_notifications_delivery_status_delivery_available_at_utc");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "delivery_status",
                table: "notifications",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                sql: "TRUNCATE TABLE notifications;");

            migrationBuilder.RenameColumn(
                name: "delivery_status",
                table: "notifications",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "delivery_sent_at_utc",
                table: "notifications",
                newName: "sent_at_utc");

            migrationBuilder.RenameColumn(
                name: "delivery_last_error",
                table: "notifications",
                newName: "last_error");

            migrationBuilder.RenameColumn(
                name: "delivery_created_at_utc",
                table: "notifications",
                newName: "created_at_utc");

            migrationBuilder.RenameColumn(
                name: "delivery_available_at_utc",
                table: "notifications",
                newName: "available_at_utc");

            migrationBuilder.RenameColumn(
                name: "delivery_attempts",
                table: "notifications",
                newName: "attempts");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_delivery_status_delivery_sent_at_utc",
                table: "notifications",
                newName: "ix_notifications_status_sent_at_utc");

            migrationBuilder.RenameIndex(
                name: "ix_notifications_delivery_status_delivery_available_at_utc",
                table: "notifications",
                newName: "ix_notifications_status_available_at_utc");

            migrationBuilder.AlterColumn<string>(
                name: "kind",
                table: "notifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "notifications",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);
        }
    }
}
