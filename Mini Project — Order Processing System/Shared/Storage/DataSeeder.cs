using OrderProcessing.Shared.Models;

namespace OrderProcessing.Shared.Storage;

public static class DataSeeder
{
    public static async Task ResetAsync(JsonFileStore? store = null, CancellationToken ct = default)
    {
        store ??= new JsonFileStore();

        if (Directory.Exists(DataFiles.DataDirectory))
        {
            try
            {
                Directory.Delete(DataFiles.DataDirectory, recursive: true);
            }
            catch (IOException)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
                if (Directory.Exists(DataFiles.DataDirectory))
                {
                    Directory.Delete(DataFiles.DataDirectory, recursive: true);
                }
            }
        }

        Directory.CreateDirectory(DataFiles.DataDirectory);
        Directory.CreateDirectory(DataFiles.InvoicesPath);
        Directory.CreateDirectory(DataFiles.ReportsPath);

        // 1. customers.json (10 records)
        var customers = GetSeedCustomers();
        await store.WriteAllAsync(DataFiles.Customers, customers, ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Customers} -> {customers.Count} records");

        // 2. products.json (8 records)
        var products = GetSeedProducts();
        await store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Products} -> {products.Count} records");

        // 3. coupons.json (4 records)
        var coupons = GetSeedCoupons();
        await store.WriteAllAsync(DataFiles.Coupons, coupons, ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Coupons} -> {coupons.Count} records");

        // 4. incoming_orders.json (20 records)
        var incomingOrders = GetSeedIncomingOrders();
        await store.WriteAllAsync(DataFiles.IncomingOrders, incomingOrders, ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.IncomingOrders} -> {incomingOrders.Count} records");

        // 5. Empty collections ([] at start)
        await store.WriteAllAsync(DataFiles.Orders, new List<Order>(), ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Orders} -> 0 records");

        await store.WriteAllAsync(DataFiles.Payments, new List<PaymentTransaction>(), ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Payments} -> 0 records");

        await store.WriteAllAsync(DataFiles.Emails, new List<EmailMessage>(), ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.Emails} -> 0 records");

        await store.WriteAllAsync(DataFiles.EmailRetry, new List<EmailMessage>(), ct).ConfigureAwait(false);
        Console.WriteLine($"[Seeder] {DataFiles.EmailRetry} -> 0 records");
    }

    private static List<Customer> GetSeedCustomers() => new()
    {
        new Customer { Id = 1, Name = "Ahmed Ali", Email = "ahmed.ali@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 2, Name = "Sara Mohamed", Email = "sara.mohamed@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 3, Name = "Omar Hassan", Email = "omar.hassan@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 4, Name = "Mona Ibrahim", Email = "mona.ibrahim@example.com", IsActive = false, LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 5, Name = "Youssef Adel", Email = "youssef.adel@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 6, Name = "Nour Khaled", Email = "nour.khaled@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 7, Name = "Karim Samir", Email = "karim.samir@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Silver },
        new Customer { Id = 8, Name = "Laila Mahmoud", Email = "laila.mahmoud@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Gold },
        new Customer { Id = 9, Name = "Hassan Tarek", Email = "hassan.tarek@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Bronze },
        new Customer { Id = 10, Name = "Dina Fathy", Email = "dina.fathy@example.com", IsActive = true, LoyaltyTier = LoyaltyTier.Silver }
    };

    private static List<Product> GetSeedProducts() => new()
    {
        new Product { Id = 1, Name = "Laptop", Price = 1200.00m, StockQuantity = 10, WeightKg = 2.2m },
        new Product { Id = 2, Name = "Smartphone", Price = 800.00m, StockQuantity = 15, WeightKg = 0.4m },
        new Product { Id = 3, Name = "Wireless Mouse", Price = 25.00m, StockQuantity = 2, WeightKg = 0.1m },
        new Product { Id = 4, Name = "Mechanical Keyboard", Price = 90.00m, StockQuantity = 1, WeightKg = 0.9m },
        new Product { Id = 5, Name = "USB-C Hub", Price = 45.00m, StockQuantity = 30, WeightKg = 0.2m },
        new Product { Id = 6, Name = "Headphones", Price = 150.00m, StockQuantity = 12, WeightKg = 0.3m },
        new Product { Id = 7, Name = "27\" Monitor", Price = 350.00m, StockQuantity = 6, WeightKg = 5.0m },
        new Product { Id = 8, Name = "Webcam", Price = 70.00m, StockQuantity = 20, WeightKg = 0.2m }
    };

    private static List<Coupon> GetSeedCoupons() => new()
    {
        new Coupon { Code = "SAVE10", DiscountPercent = 10, ExpiryDate = DateTime.Today.AddDays(180) },
        new Coupon { Code = "WELCOME15", DiscountPercent = 15, ExpiryDate = DateTime.Today.AddDays(90) },
        new Coupon { Code = "FLASH20", DiscountPercent = 20, ExpiryDate = DateTime.Today.AddDays(7) },
        new Coupon { Code = "OLD20", DiscountPercent = 20, ExpiryDate = DateTime.Today.AddDays(-30) }
    };

    private static List<Order> GetSeedIncomingOrders()
    {
        var now = DateTime.Now;
        return new List<Order>
        {
            new() { Id = 1, CustomerId = 1, CouponCode = "SAVE10", CreatedAt = now, Items = new() { new() { ProductId = 1, Quantity = 1 }, new() { ProductId = 3, Quantity = 1 } } },
            new() { Id = 2, CustomerId = 2, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 2, Quantity = 1 } } },
            new() { Id = 3, CustomerId = 3, CouponCode = "WELCOME15", CreatedAt = now, Items = new() { new() { ProductId = 5, Quantity = 2 }, new() { ProductId = 8, Quantity = 1 } } },
            new() { Id = 4, CustomerId = 4, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 6, Quantity = 1 } } },
            new() { Id = 5, CustomerId = 5, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 7, Quantity = 1 } } },
            new() { Id = 6, CustomerId = 6, CouponCode = "FLASH20", CreatedAt = now, Items = new() { new() { ProductId = 5, Quantity = 1 } } },
            new() { Id = 7, CustomerId = 7, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 6, Quantity = 1 }, new() { ProductId = 8, Quantity = 1 } } },
            new() { Id = 8, CustomerId = 8, CouponCode = "SAVE10", CreatedAt = now, Items = new() { new() { ProductId = 1, Quantity = 1 } } },
            new() { Id = 9, CustomerId = 9, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 3, Quantity = 5 } } },
            new() { Id = 10, CustomerId = 10, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 4, Quantity = 1 } } },
            new() { Id = 11, CustomerId = 1, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 4, Quantity = 1 } } },
            new() { Id = 12, CustomerId = 2, CouponCode = "OLD20", CreatedAt = now, Items = new() { new() { ProductId = 2, Quantity = 1 } } },
            new() { Id = 13, CustomerId = 3, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 6, Quantity = 2 } } },
            new() { Id = 14, CustomerId = 5, CouponCode = "WELCOME15", CreatedAt = now, Items = new() { new() { ProductId = 8, Quantity = 2 }, new() { ProductId = 5, Quantity = 1 } } },
            new() { Id = 15, CustomerId = 99, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 2, Quantity = 1 } } },
            new() { Id = 16, CustomerId = 6, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 7, Quantity = 1 }, new() { ProductId = 5, Quantity = 1 } } },
            new() { Id = 17, CustomerId = 7, CouponCode = "SAVE10", CreatedAt = now, Items = new() { new() { ProductId = 2, Quantity = 1 } } },
            new() { Id = 18, CustomerId = 8, CouponCode = "FAKE50", CreatedAt = now, Items = new() { new() { ProductId = 6, Quantity = 1 } } },
            new() { Id = 19, CustomerId = 9, CouponCode = null, CreatedAt = now, Items = new() { new() { ProductId = 8, Quantity = 1 } } },
            new() { Id = 20, CustomerId = 10, CouponCode = "FLASH20", CreatedAt = now, Items = new() { new() { ProductId = 1, Quantity = 1 }, new() { ProductId = 6, Quantity = 1 } } }
        };
    }
}

