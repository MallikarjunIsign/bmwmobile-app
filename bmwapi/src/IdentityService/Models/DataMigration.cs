namespace IdentityService.Models;

/// <summary>A one-time data change that has run (e.g. giving existing users their buyer / seller role).</summary>
public class DataMigration
{
    public required string Name { get; set; }
    public DateTime AppliedAt { get; set; }
}
