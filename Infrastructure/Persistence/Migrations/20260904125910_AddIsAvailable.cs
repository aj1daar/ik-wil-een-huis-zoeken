using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWEHZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAvailable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE rental_listings ADD COLUMN IF NOT EXISTS is_available boolean NOT NULL DEFAULT TRUE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_available",
                table: "rental_listings");
        }
    }
}
