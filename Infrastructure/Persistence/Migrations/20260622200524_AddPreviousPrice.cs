using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWEHZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreviousPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "previous_price",
                table: "rental_listings",
                type: "numeric(10,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "previous_price",
                table: "rental_listings");
        }
    }
}
