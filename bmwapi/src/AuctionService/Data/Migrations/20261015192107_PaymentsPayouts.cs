using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ArrivedAmount",
                table: "PaymentTransfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArrivedCurrency",
                table: "PaymentTransfers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "PaymentTransfers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAttemptAt",
                table: "PaymentTransfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SellerAmount",
                table: "PaymentTransfers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "SellerCurrency",
                table: "PaymentTransfers",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "BalanceAmount",
                table: "Payments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BalanceCurrency",
                table: "Payments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardLabel",
                table: "Payments",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputeStatus",
                table: "Payments",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "Payments",
                type: "numeric(18,8)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderDisputeId",
                table: "Payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RefundedAmount",
                table: "Payments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "AutoCompleteAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HoldReason",
                table: "Orders",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OnHoldSince",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedAmount",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "ArrivedCurrency",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "LastAttemptAt",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "SellerAmount",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "SellerCurrency",
                table: "PaymentTransfers");

            migrationBuilder.DropColumn(
                name: "BalanceAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BalanceCurrency",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CardLabel",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "DisputeStatus",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ProviderDisputeId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "AutoCompleteAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "HoldReason",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "OnHoldSince",
                table: "Orders");
        }
    }
}
