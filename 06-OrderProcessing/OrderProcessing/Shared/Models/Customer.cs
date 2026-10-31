using System.Text.Json.Serialization;

namespace OrderProcessing.Shared.Models;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public LoyaltyTier LoyaltyTier { get; set; }

    [JsonIgnore]
    public bool Active
    {
        get => IsActive;
        set => IsActive = value;
    }

    [JsonIgnore]
    public LoyaltyTier Tier
    {
        get => LoyaltyTier;
        set => LoyaltyTier = value;
    }

    public Customer() { }
}
