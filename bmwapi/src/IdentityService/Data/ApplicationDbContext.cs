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

        builder.Entity<TrustedDevice>(d =>
        {
            d.Property(x => x.DeviceId).HasMaxLength(100);
            d.Property(x => x.TokenHash).HasMaxLength(64);
            d.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
            d.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
