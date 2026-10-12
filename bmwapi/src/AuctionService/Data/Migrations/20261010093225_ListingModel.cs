using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AuctionService.Data.Migrations
{
    /// <inheritdoc />
    public partial class ListingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "Items",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Items",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryOptions",
                table: "Items",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DurationDays",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FitmentMode",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PartTypeId",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SizeClass",
                table: "Items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Items",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TitleExtra",
                table: "Items",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Angle",
                table: "ItemImages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailFileName",
                table: "ItemImages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "ItemImages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BidCount",
                table: "Auctions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SellerEmail",
                table: "Auctions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerPhone",
                table: "Auctions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    LegacyCategory = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Categories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GarageVehicles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Make = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Trim = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Engine = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Nickname = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GarageVehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Makes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    Countries = table.Column<string[]>(type: "text[]", nullable: false),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Makes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    AuctionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Seller = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Buyer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinalTotal = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Title = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LocationName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DeliveryOptions = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    HandedOverAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SellerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SellerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    BuyerEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BuyerPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PartDetails",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    BrandType = table.Column<int>(type: "integer", nullable: true),
                    Brand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Position = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    UsedGrade = table.Column<int>(type: "integer", nullable: true),
                    DonorMileage = table.Column<int>(type: "integer", nullable: true),
                    Colour = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PaintCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    Includes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Warranty = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartDetails", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_PartDetails_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartFitments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    YearFrom = table.Column<int>(type: "integer", nullable: false),
                    YearTo = table.Column<int>(type: "integer", nullable: false),
                    MakeId = table.Column<int>(type: "integer", nullable: true),
                    Make = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ModelId = table.Column<int>(type: "integer", nullable: true),
                    Model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Trim = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Engine = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Fuel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Transmission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Drivetrain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SteeringSide = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Market = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartFitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartFitments_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartNumbers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Normalized = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartNumbers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartNumbers_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartSpecs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartSpecs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartSpecs_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleDetails",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Trim = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    BodyType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Fuel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    EngineSize = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Transmission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Drivetrain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SteeringSide = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Vin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RunningStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    MileageUnit = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DamageType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DamageAreas = table.Column<string[]>(type: "text[]", nullable: false),
                    Keys = table.Column<int>(type: "integer", nullable: true),
                    Owners = table.Column<int>(type: "integer", nullable: true),
                    ServiceHistory = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    InspectionAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    PapersJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleDetails", x => x.ItemId);
                    table.ForeignKey(
                        name: "FK_VehicleDetails_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PartTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    FitmentMode = table.Column<int>(type: "integer", nullable: false),
                    HasPosition = table.Column<bool>(type: "boolean", nullable: false),
                    HasSide = table.Column<bool>(type: "boolean", nullable: false),
                    IsBodyPart = table.Column<bool>(type: "boolean", nullable: false),
                    Synonyms = table.Column<string[]>(type: "text[]", nullable: false),
                    ImportantFitmentFields = table.Column<string[]>(type: "text[]", nullable: false),
                    SpecFieldsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartTypes_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Models",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MakeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Aliases = table.Column<string[]>(type: "text[]", nullable: false),
                    Countries = table.Column<string[]>(type: "text[]", nullable: false),
                    BodyType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Models_Makes_MakeId",
                        column: x => x.MakeId,
                        principalTable: "Makes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModelId = table.Column<int>(type: "integer", nullable: false),
                    YearFrom = table.Column<int>(type: "integer", nullable: false),
                    YearTo = table.Column<int>(type: "integer", nullable: false),
                    Trim = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Engine = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Fuel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Transmission = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Drivetrain = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Market = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleConfigs_Models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "Models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_PartTypeId",
                table: "Items",
                column: "PartTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_Seller_Status",
                table: "Auctions",
                columns: new[] { "Seller", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentId_Name",
                table: "Categories",
                columns: new[] { "ParentId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GarageVehicles_User",
                table: "GarageVehicles",
                column: "User");

            migrationBuilder.CreateIndex(
                name: "IX_Makes_Name",
                table: "Makes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Models_MakeId_Name",
                table: "Models",
                columns: new[] { "MakeId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_AuctionId",
                table: "Orders",
                column: "AuctionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Buyer",
                table: "Orders",
                column: "Buyer");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Seller",
                table: "Orders",
                column: "Seller");

            migrationBuilder.CreateIndex(
                name: "IX_PartFitments_ItemId",
                table: "PartFitments",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PartNumbers_ItemId",
                table: "PartNumbers",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PartNumbers_Normalized",
                table: "PartNumbers",
                column: "Normalized");

            migrationBuilder.CreateIndex(
                name: "IX_PartSpecs_ItemId",
                table: "PartSpecs",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PartTypes_CategoryId",
                table: "PartTypes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PartTypes_Name",
                table: "PartTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleConfigs_ModelId",
                table: "VehicleConfigs",
                column: "ModelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_PartTypes_PartTypeId",
                table: "Items",
                column: "PartTypeId",
                principalTable: "PartTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Items_PartTypes_PartTypeId",
                table: "Items");

            migrationBuilder.DropTable(
                name: "GarageVehicles");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "PartDetails");

            migrationBuilder.DropTable(
                name: "PartFitments");

            migrationBuilder.DropTable(
                name: "PartNumbers");

            migrationBuilder.DropTable(
                name: "PartSpecs");

            migrationBuilder.DropTable(
                name: "PartTypes");

            migrationBuilder.DropTable(
                name: "VehicleConfigs");

            migrationBuilder.DropTable(
                name: "VehicleDetails");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Models");

            migrationBuilder.DropTable(
                name: "Makes");

            migrationBuilder.DropIndex(
                name: "IX_Items_PartTypeId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Auctions_Seller_Status",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DeliveryOptions",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "DurationDays",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "FitmentMode",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "PartTypeId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "SizeClass",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TitleExtra",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Angle",
                table: "ItemImages");

            migrationBuilder.DropColumn(
                name: "ThumbnailFileName",
                table: "ItemImages");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "ItemImages");

            migrationBuilder.DropColumn(
                name: "BidCount",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "SellerEmail",
                table: "Auctions");

            migrationBuilder.DropColumn(
                name: "SellerPhone",
                table: "Auctions");
        }
    }
}
