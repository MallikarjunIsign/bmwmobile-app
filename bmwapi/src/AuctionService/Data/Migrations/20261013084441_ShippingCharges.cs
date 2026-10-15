using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ShippingCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Address_Latitude",
                table: "ShippingAddresses",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Address_Longitude",
                table: "ShippingAddresses",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShipTo_Latitude",
                table: "Orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ShipTo_Longitude",
                table: "Orders",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingDistance",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingDistanceUnit",
                table: "Orders",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ShippingSizeClass",
                table: "Orders",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShippingRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    SizeClass = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Unit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    BaseFee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    RatePerUnit = table.Column<decimal>(type: "numeric(12,4)", nullable: false),
                    MinFee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    MaxFee = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    MaxDistance = table.Column<int>(type: "integer", nullable: false),
                    IsPlaceholder = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShippingRates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShippingRates_CountryCode_SizeClass",
                table: "ShippingRates",
                columns: new[] { "CountryCode", "SizeClass" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShippingRates");

            migrationBuilder.DropColumn(
                name: "Address_Latitude",
                table: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "Address_Longitude",
                table: "ShippingAddresses");

            migrationBuilder.DropColumn(
                name: "ShipTo_Latitude",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShipTo_Longitude",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingDistance",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingDistanceUnit",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingSizeClass",
                table: "Orders");
        }
    }
}
