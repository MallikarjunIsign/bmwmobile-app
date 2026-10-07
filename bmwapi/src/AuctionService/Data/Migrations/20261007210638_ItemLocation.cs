using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <summary>
    /// Item location (name + coordinates) for "parts within X km".
    /// Only these three columns: the PartRequests / Purchases entities are still left out of the
    /// migrations on purpose (unchanged from before), so the demo endpoints keep their mock fallback.
    /// </summary>
    public partial class ItemLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Items",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                table: "Items",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Items",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "LocationName",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Items");
        }
    }
}
