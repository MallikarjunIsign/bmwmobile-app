using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatService.Data.Migrations
{
    /// <inheritdoc />
    public partial class PushDeviceLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CountryCode",
                table: "PushDevices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "PushDevices",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "PushDevices",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountryCode",
                table: "PushDevices");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "PushDevices");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "PushDevices");
        }
    }
}
