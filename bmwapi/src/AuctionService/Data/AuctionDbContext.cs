using AuctionService.Entities;
using Microsoft.EntityFrameworkCore;

namespace AuctionService.Data;

public class AuctionDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<Auction> Auctions { get; set; }
    public DbSet<Container> Containers { get; set; }
    public DbSet<ItemImage> ItemImages { get; set; }
    public DbSet<PartRequest> PartRequests { get; set; }
    public DbSet<Purchase> Purchases { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Auction>()
            .HasOne(x => x.Item)
            .WithOne(x => x.Auction)
            .HasForeignKey<Item>(x => x.Id)
            .IsRequired();

        modelBuilder.Entity<Auction>().Property(x => x.StartingPrice).HasDefaultValue(1);
        modelBuilder.Entity<Auction>().Property(x => x.BidIncrement).HasDefaultValue(1);

        modelBuilder.Entity<Item>().Property(x => x.ItemType).HasDefaultValue(ItemType.Vehicle);

        modelBuilder.Entity<Item>()
            .HasMany(x => x.Images)
            .WithOne(x => x.Item)
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        // A container that is still used by auctions cannot be deleted.
        modelBuilder.Entity<Item>()
            .HasOne(x => x.Container)
            .WithMany(x => x.Items)
            .HasForeignKey(x => x.ContainerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Container>()
            .HasIndex(x => x.ContainerNumber)
            .IsUnique();
    }
}