using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <summary>
    /// Custom orders (PartRequests: search text, part number, buyer location/radius, Open/Matched/Closed) and the
    /// Purchases table. Both entities existed in code but were never migrated; this only ADDS the two tables.
    /// </summary>
    public partial class CustomOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartRequests",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SearchText = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Make = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    PartName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PartNumberNormalized = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Vin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Specs = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MaxBudget = table.Column<decimal>(type: "numeric", nullable: false),
                    LocationName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    RadiusKm = table.Column<double>(type: "double precision", nullable: true),
                    PreferredPort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FeasibilityNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EstimatedDeliveryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MatchedAuctionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    MatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    AuctionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Buyer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    WinningBidAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    BuyerFee = table.Column<decimal>(type: "numeric", nullable: false),
                    ShippingFee = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    DeliveryOption = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DeliveryAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PickupPort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PaymentMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    QrPassCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ShippingStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ContainerNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
                    RatingComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartRequests");

            migrationBuilder.DropTable(
                name: "Purchases");
        }
    }
}
