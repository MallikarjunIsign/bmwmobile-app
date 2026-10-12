using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryOptionsShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartPrice",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddress",
                table: "Orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_City",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_CountryCode",
                table: "Orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Line1",
                table: "Orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Line2",
                table: "Orders",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Name",
                table: "Orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Phone",
                table: "Orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Postcode",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingFee",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupAddress",
                table: "Items",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingFee",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingAddresses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address_Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Address_Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Address_Line1 = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Address_Line2 = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    Address_City = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Address_Postcode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Address_CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingAddresses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingAddresses_User",
                table: "ShippingAddresses",
                column: "User");

            // "Buyer arranges" (4) was removed: listings and orders that offered it offer Pickup (1) instead.
            // "Seller ships" (2) is now "Shipping" (same value); its fee stays unknown (null) until the seller sets it.
            migrationBuilder.Sql("""
                UPDATE "Items"  SET "DeliveryOptions" = ("DeliveryOptions" & ~4) | 1 WHERE "DeliveryOptions" & 4 <> 0;
                UPDATE "Orders" SET "DeliveryOptions" = ("DeliveryOptions" & ~4) | 1 WHERE "DeliveryOptions" & 4 <> 0;
                UPDATE "Orders" SET "DeliveryMethod" = 1 WHERE "DeliveryMethod" = 4;
                """);

            // Removed order steps: Packed (3) → Sold (0), Out for delivery (5) → Shipped (4).
            // "Handed over" (1) was only used for buyer's couriers, now Pickup: it becomes "Picked up" (same value).
            migrationBuilder.Sql("""
                UPDATE "Orders"      SET "Status" = 0 WHERE "Status" = 3;
                UPDATE "Orders"      SET "Status" = 4 WHERE "Status" = 5;
                UPDATE "OrderEvents" SET "Status" = 0 WHERE "Status" = 3;
                UPDATE "OrderEvents" SET "Status" = 4 WHERE "Status" = 5;
                """);

            // Existing orders: the price breakdown and the listing country.
            migrationBuilder.Sql("""
                UPDATE "Orders" o SET "PartPrice" = a."StartingPrice" * o."Quantity", "CountryCode" = i."CountryCode"
                FROM "Auctions" a JOIN "Items" i ON i."Id" = a."Id"
                WHERE a."Id" = o."AuctionId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PartPrice",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupAddress",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_City",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_CountryCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Line1",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Line2",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Name",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Phone",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Postcode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PickupAddress",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "Items");
        }
    }
}
