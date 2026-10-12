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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(u =>
        {
            u.Property(x => x.FullName).HasMaxLength(100);
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
