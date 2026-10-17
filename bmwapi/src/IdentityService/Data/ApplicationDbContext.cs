using IdentityService.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();
    public DbSet<DataMigration> DataMigrations => Set<DataMigration>();
    public DbSet<SellerApplication> SellerApplications => Set<SellerApplication>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(u =>
        {
            u.Property(x => x.FullName).HasMaxLength(100);
            u.Property(x => x.BusinessName).HasMaxLength(100);
            u.Property(x => x.CountryCode).HasMaxLength(2);
            u.Property(x => x.City).HasMaxLength(100);
        });

        builder.Entity<DataMigration>(m =>
        {
            m.HasKey(x => x.Name);
            m.Property(x => x.Name).HasMaxLength(100);
        });

        builder.Entity<SellerApplication>(a =>
        {
            a.Property(x => x.FullName).HasMaxLength(100);
            a.Property(x => x.Email).HasMaxLength(256);
            a.Property(x => x.PhoneNumber).HasMaxLength(20);
            a.Property(x => x.CountryCode).HasMaxLength(2);
            a.Property(x => x.City).HasMaxLength(100);
            a.Property(x => x.BusinessName).HasMaxLength(100);
            a.Property(x => x.BusinessType).HasMaxLength(20);
            a.Property(x => x.Sells).HasMaxLength(10);
            a.Property(x => x.BusinessAddress).HasMaxLength(250);
            a.Property(x => x.TaxId).HasMaxLength(30);
            a.Property(x => x.Message).HasMaxLength(1000);
            a.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            a.Property(x => x.ReviewedBy).HasMaxLength(256);
            a.Property(x => x.AdminNote).HasMaxLength(500);
            a.HasIndex(x => new { x.Status, x.CreatedAt });
            a.HasIndex(x => x.Email);
            a.HasIndex(x => x.UserId);
        });

        builder.Entity<OtpCode>(o =>
        {
            o.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(20);
            o.Property(x => x.CodeHash).HasMaxLength(64);
            o.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedAt });
            o.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserLocation>(l =>
        {
            l.Property(x => x.Label).HasMaxLength(40);
            l.Property(x => x.PlaceName).HasMaxLength(120);
            l.Property(x => x.AddressLine).HasMaxLength(200);
            l.Property(x => x.Area).HasMaxLength(100);
            l.Property(x => x.Landmark).HasMaxLength(100);
            l.Property(x => x.PostalCode).HasMaxLength(12);
            l.Property(x => x.CountryCode).HasMaxLength(2);
            l.Property(x => x.PlaceId).HasMaxLength(300);
            l.Property(x => x.FormattedAddress).HasMaxLength(300);
            l.Property(x => x.City).HasMaxLength(100);
            l.Property(x => x.State).HasMaxLength(100);
            l.HasIndex(x => x.UserId);
            // At most one primary location per user.
            l.HasIndex(x => x.UserId).IsUnique().HasFilter("\"IsPrimary\" = true").HasDatabaseName("IX_UserLocations_UserId_Primary");
            l.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<TrustedDevice>(d =>
        {
            d.Property(x => x.DeviceId).HasMaxLength(100);
            d.Property(x => x.TokenHash).HasMaxLength(64);
            d.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
            d.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
