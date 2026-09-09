using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWEHZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS so a re-run against a database that already has the columns is a
            // no-op instead of a startup crash loop (same reason as AddIsAvailable).
            migrationBuilder.Sql(
                "ALTER TABLE notification_logs ADD COLUMN IF NOT EXISTS message_id integer;");
            migrationBuilder.Sql(
                "ALTER TABLE notification_logs ADD COLUMN IF NOT EXISTS retracted_at timestamp with time zone;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "message_id",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "retracted_at",
                table: "notification_logs");
        }
    }
}
