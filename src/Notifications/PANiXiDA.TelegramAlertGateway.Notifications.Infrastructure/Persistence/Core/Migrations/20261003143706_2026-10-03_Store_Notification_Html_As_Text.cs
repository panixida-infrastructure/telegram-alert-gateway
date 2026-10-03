using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PANiXiDA.TelegramAlertGateway.Notifications.Infrastructure.Persistence.Core.Migrations
{
    /// <inheritdoc />
    public partial class _20261003_Store_Notification_Html_As_Text : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "notifications",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(3900)",
                oldMaxLength: 3900);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "message",
                table: "notifications",
                type: "character varying(3900)",
                maxLength: 3900,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");
        }
    }
}
