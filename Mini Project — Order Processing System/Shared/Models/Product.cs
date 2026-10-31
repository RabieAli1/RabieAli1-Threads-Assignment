using System.Text.Json.Serialization;

namespace OrderProcessing.Shared.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public decimal WeightKg { get; set; }

    [JsonIgnore]
    public int Stock
    {
        get => StockQuantity;
        set => StockQuantity = value;
    }

    public Product() { }
}
