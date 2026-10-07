using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PartsContainersImagesPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Mileage",
                table: "Items",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<int>(
                name: "Condition",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContainerId",
                table: "Items",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemType",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PartCategory",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartName",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PartNumber",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "YearTo",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidIncrement",
                table: "Auctions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "EndReason",
                table: "Auctions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SellerDecisionDeadline",
                table: "Auctions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StartingPrice",
                table: "Auctions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Containers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ContainerNumber = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: false),
                    OriginPort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OriginCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DestinationPort = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DestinationCountry = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DepartureDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedArrivalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ShippingStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Containers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemImages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemImages_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_ContainerId",
                table: "Items",
                column: "ContainerId");

            migrationBuilder.CreateIndex(
                name: "IX_Containers_ContainerNumber",
                table: "Containers",
                column: "ContainerNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemImages_ItemId",
                table: "ItemImages",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Containers_ContainerId",
                table: "Items",
                column: "ContainerId",
                principalTable: "Containers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Existing data: each item's ImageUrl becomes its primary ItemImage (external URL).
            migrationBuilder.Sql("""
                INSERT INTO "ItemImages" ("Id", "ItemId", "Url", "FileName", "SortOrder", "IsPrimary", "CreatedAt")
                SELECT gen_random_uuid()::text, i."Id", i."ImageUrl",
                       left(regexp_replace(i."ImageUrl", '^.*/', ''), 200), 0, true, now()
                FROM "Items" i
                WHERE coalesce(i."ImageUrl", '') <> '';
                """);

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Items");

            // Existing auctions keep today's behaviour: any bid above the current high bid is valid
            // (StartingPrice = BidIncrement = 1), and "no reserve" (0) becomes reserve = starting price.
            migrationBuilder.Sql("""UPDATE "Auctions" SET "ReservePrice" = GREATEST("ReservePrice", "StartingPrice");""");

            // Status values: Live = 0, Finished (1) is now Sold, ReserveNotMet = 2, Unsold = 3 (new).
            // Old ReserveNotMet auctions have no grace period, so they become Sold (if there was a
            // winner) or Unsold.
            migrationBuilder.Sql("""
                UPDATE "Auctions" SET "Status" = CASE WHEN "Winner" IS NOT NULL THEN 1 ELSE 3 END, "EndReason" = 0
                WHERE "Status" = 2;
                UPDATE "Auctions" SET "EndReason" = 0 WHERE "Status" = 1 AND "EndReason" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_Containers_ContainerId",
                table: "Items");

            migrationBuilder.DropTable(
                name: "Containers");

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Uploaded images are stored as relative paths; only external URLs fit the old model.
            migrationBuilder.Sql("""
                UPDATE "Items" i SET "ImageUrl" = left(img."Url", 200)
                FROM "ItemImages" img
                WHERE img."ItemId" = i."Id" AND img."IsPrimary";
                UPDATE "Auctions" SET "Status" = 2 WHERE "Status" = 3;
                UPDATE "Items" SET "Mileage" = coalesce("Mileage", 0), "Color" = coalesce("Color", '');
                """);

            migrationBuilder.DropTable(
                name: "ItemImages");

            migrationBuilder.DropIndex(
                name: "IX_Items_ContainerId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Condition",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ContainerId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PartCategory",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PartName",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PartNumber",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "YearTo",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "BidIncrement",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "SellerDecisionDeadline",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "StartingPrice",
                table: "Auctions");

            migrationBuilder.AlterColumn<int>(
                name: "Mileage",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "Items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}
