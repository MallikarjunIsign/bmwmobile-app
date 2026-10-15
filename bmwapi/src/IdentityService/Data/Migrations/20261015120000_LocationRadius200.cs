using IdentityService.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityService.Data.Migrations
{
    /// <summary>
    /// Data only (no schema change): the default radius is now 200 km / 200 mi. Saved locations that still have the
    /// previous default – 60 km, or 60 miles (stored as 96 / 97 km) in the USA – get 200. Those values were never a
    /// choice in the app (the earlier radius chips were 5, 10, 25, 50, 100, 200 km and 5–100 mi), so only defaults
    /// are changed. Everything else – including 50 km, which was both the older default and a chip – is kept.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261015120000_LocationRadius200")]
    public partial class LocationRadius200 : Migration
    {
        /// <summary>200 mi = 321.87 km, stored as whole km.</summary>
        public const string UsaSql = """
            UPDATE "UserLocations" SET "RadiusKm" = 322, "UpdatedAt" = now()
            WHERE "CountryCode" = 'US' AND "RadiusKm" IN (96, 97);
            """;

        public const string OtherCountriesSql = """
            UPDATE "UserLocations" SET "RadiusKm" = 200, "UpdatedAt" = now()
            WHERE COALESCE("CountryCode", '') <> 'US' AND "RadiusKm" = 60;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UsaSql);
            migrationBuilder.Sql(OtherCountriesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Can't tell which 200s were defaults: left as they are.
        }
    }
}
