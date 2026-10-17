using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsBuyers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BuyerEmail",
                table: "StockReservations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPhone",
                table: "StockReservations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryMethod",
                table: "StockReservations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_City",
                table: "StockReservations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_CountryCode",
                table: "StockReservations",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShipTo_Latitude",
                table: "StockReservations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Line1",
                table: "StockReservations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Line2",
                table: "StockReservations",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShipTo_Longitude",
                table: "StockReservations",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Name",
                table: "StockReservations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Phone",
                table: "StockReservations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShipTo_Postcode",
                table: "StockReservations",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingDistance",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingDistanceUnit",
                table: "StockReservations",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingFee",
                table: "StockReservations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ShippingRateEstimated",
                table: "StockReservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShippingService",
                table: "StockReservations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingZone",
                table: "StockReservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "OrderId",
                table: "Payments",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36);

            migrationBuilder.AddColumn<string>(
                name: "AuctionId",
                table: "Payments",
                type: "character varying(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReservationId",
                table: "Payments",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalReminderAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HalfwayReminderAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaymentDueAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<int>(
                name: "SecondChanceRound",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "QuantityReserved",
                table: "Auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReservationId",
                table: "Payments",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_PaymentDueAt",
                table: "Orders",
                columns: new[] { "Status", "PaymentDueAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payments_ReservationId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_PaymentDueAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerEmail",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "BuyerPhone",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "DeliveryMethod",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_City",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_CountryCode",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Latitude",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Line1",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Line2",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Longitude",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Name",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Phone",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShipTo_Postcode",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingDistance",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingDistanceUnit",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingFee",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingRateEstimated",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingService",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "ShippingZone",
                table: "StockReservations");

            migrationBuilder.DropColumn(
                name: "AuctionId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ReservationId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FinalReminderAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HalfwayReminderAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentDueAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SecondChanceRound",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "QuantityReserved",
                table: "Auctions");

            migrationBuilder.AlterColumn<string>(
                name: "OrderId",
                table: "Payments",
                type: "character varying(36)",
                maxLength: 36,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);
        }
    }
}
