using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatService.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserRatings",
                columns: table => new
                {
                    User = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Average = table.Column<double>(type: "double precision", nullable: true),
                    Count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRatings", x => x.User);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRatings");
        }
    }
}
