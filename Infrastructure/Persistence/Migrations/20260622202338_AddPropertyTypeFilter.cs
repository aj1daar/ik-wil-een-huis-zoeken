using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IWEHZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPropertyTypeFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "property_type_filter",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "property_type_filter",
                table: "users");
        }
    }
}
