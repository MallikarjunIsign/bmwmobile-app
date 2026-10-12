using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class Reviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "SellerRatingAverage",
                table: "Auctions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SellerRatingCount",
                table: "Auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ReviewReports",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ReviewId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Reporter = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewReports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ListingId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Buyer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Seller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemTitle = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ItemImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ItemRating = table.Column<int>(type: "integer", nullable: false),
                    SellerRating = table.Column<int>(type: "integer", nullable: false),
                    Communication = table.Column<int>(type: "integer", nullable: true),
                    Accuracy = table.Column<int>(type: "integer", nullable: true),
                    Delivery = table.Column<int>(type: "integer", nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SellerReply = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SellerReplyAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Photos = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SellerRatings",
                columns: table => new
                {
                    Seller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Average = table.Column<double>(type: "double precision", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Stars1 = table.Column<int>(type: "integer", nullable: false),
                    Stars2 = table.Column<int>(type: "integer", nullable: false),
                    Stars3 = table.Column<int>(type: "integer", nullable: false),
                    Stars4 = table.Column<int>(type: "integer", nullable: false),
                    Stars5 = table.Column<int>(type: "integer", nullable: false),
                    CommunicationAverage = table.Column<double>(type: "double precision", nullable: true),
                    AccuracyAverage = table.Column<double>(type: "double precision", nullable: true),
                    DeliveryAverage = table.Column<double>(type: "double precision", nullable: true),
                    ItemAverage = table.Column<double>(type: "double precision", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SellerRatings", x => x.Seller);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewReports_Status",
                table: "ReviewReports",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ListingId",
                table: "Reviews",
                column: "ListingId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_OrderId",
                table: "Reviews",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_Seller_CreatedAt",
                table: "Reviews",
                columns: new[] { "Seller", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewReports");

            migrationBuilder.DropTable(
                name: "Reviews");

            migrationBuilder.DropTable(
                name: "SellerRatings");

            migrationBuilder.DropColumn(
                name: "SellerRatingAverage",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "SellerRatingCount",
                table: "Auctions");
        }
    }
}
