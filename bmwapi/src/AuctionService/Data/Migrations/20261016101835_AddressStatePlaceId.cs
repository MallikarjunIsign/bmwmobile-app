using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddressStatePlaceId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShipTo_PlaceId",
                table: "StockReservations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_State",
                table: "StockReservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_PlaceId",
                table: "ShippingAddresses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Address_State",
                table: "ShippingAddresses",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_PlaceId",
                table: "Orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_State",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShipTo_PlaceId",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_State",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "Address_PlaceId",
                table: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "Address_State",
                table: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "ShipTo_PlaceId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_State",
                table: "Orders");
        }
    }
}
