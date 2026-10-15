using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class UspsShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShippingService",
                table: "Orders",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingZone",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightIn",
                table: "Items",
                type: "numeric(6,1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthIn",
                table: "Items",
                type: "numeric(6,1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightLb",
                table: "Items",
                type: "numeric(7,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthIn",
                table: "Items",
                type: "numeric(6,1)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingService",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingZone",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HeightIn",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LengthIn",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WeightLb",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "WidthIn",
                table: "Items");
        }
    }
}
