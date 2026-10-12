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

    // Master data (design doc §3, §8)
    public DbSet<Category> Categories { get; set; }
    public DbSet<PartType> PartTypes { get; set; }
    public DbSet<Make> Makes { get; set; }
    public DbSet<VehicleModel> Models { get; set; }
    public DbSet<VehicleConfig> VehicleConfigs { get; set; }

    // Listing details
    public DbSet<PartDetails> PartDetails { get; set; }
    public DbSet<PartFitment> PartFitments { get; set; }
    public DbSet<PartNumber> PartNumbers { get; set; }
    public DbSet<PartSpec> PartSpecs { get; set; }
    public DbSet<VehicleDetails> VehicleDetails { get; set; }

    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderEvent> OrderEvents { get; set; }
    public DbSet<GarageVehicle> GarageVehicles { get; set; }
    public DbSet<ShippingAddress> ShippingAddresses { get; set; }
    public DbSet<MediaFile> MediaFiles { get; set; }
    /// <summary>Blocks from ChatService (UserBlocked): a blocked user can't buy the blocker's direct sales.</summary>
    public DbSet<UserBlock> UserBlocks { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<ReviewReport> ReviewReports { get; set; }
    public DbSet<SellerRating> SellerRatings { get; set; }

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
        modelBuilder.Entity<Auction>().HasIndex(x => new { x.Seller, x.Status });

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

        // Listing details: deleted together with the item.
        modelBuilder.Entity<Item>()
            .HasOne(x => x.PartType).WithMany().HasForeignKey(x => x.PartTypeId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Item>()
            .HasOne(x => x.PartDetails).WithOne().HasForeignKey<PartDetails>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Item>()
            .HasOne(x => x.VehicleDetails).WithOne().HasForeignKey<VehicleDetails>(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Item>()
            .HasMany(x => x.Fitments).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Item>()
            .HasMany(x => x.PartNumbers).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Item>()
            .HasMany(x => x.Specs).WithOne().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<PartNumber>().HasIndex(x => x.Normalized);

        // Master data
        modelBuilder.Entity<Category>()
            .HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Category>().HasIndex(x => new { x.ParentId, x.Name }).IsUnique();
        modelBuilder.Entity<PartType>()
            .HasOne(x => x.Category).WithMany(x => x.PartTypes).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<PartType>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<Make>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<VehicleModel>()
            .HasOne(x => x.Make).WithMany(x => x.Models).HasForeignKey(x => x.MakeId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<VehicleModel>().HasIndex(x => new { x.MakeId, x.Name }).IsUnique();
        modelBuilder.Entity<VehicleConfig>()
            .HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Cascade);

        // One order per auction, but one per purchase for fixed-price listings with several units.
        modelBuilder.Entity<Order>().HasIndex(x => x.AuctionId);
        modelBuilder.Entity<Order>().HasIndex(x => x.Seller);
        modelBuilder.Entity<Order>().HasIndex(x => x.Buyer);
        modelBuilder.Entity<Order>()
            .HasMany(x => x.Events).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<GarageVehicle>().HasIndex(x => x.User);
        modelBuilder.Entity<ShippingAddress>().HasIndex(x => x.User);
        modelBuilder.Entity<MediaFile>().HasIndex(x => x.Owner);
        modelBuilder.Entity<UserBlock>().HasKey(x => new { x.Blocker, x.Blocked });

        // One review per order; listed per seller and per listing, newest first.
        modelBuilder.Entity<Review>().HasIndex(x => x.OrderId).IsUnique();
        modelBuilder.Entity<Review>().HasIndex(x => new { x.Seller, x.CreatedAt });
        modelBuilder.Entity<Review>().HasIndex(x => x.ListingId);
        modelBuilder.Entity<Review>().OwnsMany(x => x.Photos, b => b.ToJson());
        modelBuilder.Entity<ReviewReport>().HasIndex(x => x.Status);
        modelBuilder.Entity<ShippingAddress>().Navigation(x => x.Address).IsRequired();
    }
}
