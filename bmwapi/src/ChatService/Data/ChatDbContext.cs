using ChatService.Entities;
using Microsoft.EntityFrameworkCore;

namespace ChatService.Data;

public class ChatDbContext(DbContextOptions options) : DbContext(options)
{
    public DbSet<ChatListing> Listings { get; set; }
    public DbSet<ListingBuyer> ListingBuyers { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Block> Blocks { get; set; }
    public DbSet<Report> Reports { get; set; }
    public DbSet<PushDevice> PushDevices { get; set; }
    public DbSet<UserRating> UserRatings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ListingBuyer>().HasKey(x => new { x.ListingId, x.Buyer });
        modelBuilder.Entity<ChatListing>().HasMany(x => x.Buyers).WithOne().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);

        // One conversation per (listing, buyer). Listings are never deleted here (deleted ones stay readable).
        modelBuilder.Entity<Conversation>().HasIndex(x => new { x.ListingId, x.Buyer }).IsUnique();
        modelBuilder.Entity<Conversation>().HasIndex(x => new { x.Buyer, x.LastMessageAt });
        modelBuilder.Entity<Conversation>().HasIndex(x => new { x.Seller, x.LastMessageAt });
        modelBuilder.Entity<Conversation>().HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Conversation>().HasMany(x => x.Messages).WithOne().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Message>().OwnsMany(x => x.Images, b => b.ToJson());
        modelBuilder.Entity<Message>().HasIndex(x => new { x.ConversationId, x.SentAt });
        // Rate limit: a sender's messages in the last minute.
        modelBuilder.Entity<Message>().HasIndex(x => new { x.Sender, x.SentAt });

        modelBuilder.Entity<Block>().HasKey(x => new { x.Blocker, x.Blocked });
        modelBuilder.Entity<Report>().HasIndex(x => x.Status);
        modelBuilder.Entity<PushDevice>().HasIndex(x => x.User);
    }
}
