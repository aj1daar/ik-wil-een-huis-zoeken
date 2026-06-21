using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IWEHZ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    name_nl = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rental_listings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    external_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    scraped_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    telegram_chat_id = table.Column<long>(type: "bigint", nullable: false),
                    telegram_username = table.Column<string>(type: "text", nullable: true),
                    max_budget = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    onboarding_state = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    listing_id = table.Column<int>(type: "integer", nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_notification_logs_rental_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "rental_listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notification_logs_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_cities",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    city_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_cities", x => new { x.user_id, x.city_id });
                    table.ForeignKey(
                        name: "FK_user_cities_cities_city_id",
                        column: x => x.city_id,
                        principalTable: "cities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_cities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "cities",
                columns: new[] { "id", "is_active", "name_en", "name_nl" },
                values: new object[,]
                {
                    { 1, true, "Amsterdam", "Amsterdam" },
                    { 2, true, "Rotterdam", "Rotterdam" },
                    { 3, true, "The Hague", "Den Haag" },
                    { 4, true, "Utrecht", "Utrecht" },
                    { 5, true, "Eindhoven", "Eindhoven" },
                    { 6, true, "Groningen", "Groningen" },
                    { 7, true, "Tilburg", "Tilburg" },
                    { 8, true, "Almere", "Almere" },
                    { 9, true, "Breda", "Breda" },
                    { 10, true, "Nijmegen", "Nijmegen" },
                    { 11, true, "Enschede", "Enschede" },
                    { 12, true, "Apeldoorn", "Apeldoorn" },
                    { 13, true, "Haarlem", "Haarlem" },
                    { 14, true, "Arnhem", "Arnhem" },
                    { 15, true, "Zaanstad", "Zaanstad" },
                    { 16, true, "Amersfoort", "Amersfoort" },
                    { 17, true, "Maastricht", "Maastricht" },
                    { 18, true, "Dordrecht", "Dordrecht" },
                    { 19, true, "Leiden", "Leiden" },
                    { 20, true, "Zoetermeer", "Zoetermeer" },
                    { 21, true, "Zwolle", "Zwolle" },
                    { 22, true, "Deventer", "Deventer" },
                    { 23, true, "Delft", "Delft" },
                    { 24, true, "Alkmaar", "Alkmaar" },
                    { 25, true, "Den Bosch", "s-Hertogenbosch" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_cities_name_en",
                table: "cities",
                column: "name_en");

            migrationBuilder.CreateIndex(
                name: "IX_cities_name_nl",
                table: "cities",
                column: "name_nl");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_listing_id",
                table: "notification_logs",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "IX_notification_logs_user_id_listing_id",
                table: "notification_logs",
                columns: new[] { "user_id", "listing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rental_listings_external_id_source",
                table: "rental_listings",
                columns: new[] { "external_id", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_cities_city_id",
                table: "user_cities",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "IX_users_telegram_chat_id",
                table: "users",
                column: "telegram_chat_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_logs");

            migrationBuilder.DropTable(
                name: "user_cities");

            migrationBuilder.DropTable(
                name: "rental_listings");

            migrationBuilder.DropTable(
                name: "cities");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
