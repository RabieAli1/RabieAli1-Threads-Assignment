using OrderProcessing.Phase1.Sync;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncInventoryService
{
    private readonly JsonFileStore _store;

    public SyncInventoryService(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public bool ReserveStock(Order order)
    {
        Log.Info($"SyncInventoryService -> Reserving stock for Order #{order.Id} ({order.Items.Count} items)...");

        // Latency simulation (6.5 DB write)
        Log.Info($"SyncInventoryService -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbWriteOpenConnection);

        Log.Info($"SyncInventoryService -> Beginning transaction...");
        Thread.Sleep(SimulatedDelays.DbWriteBeginTransaction);

        Log.Info($"SyncInventoryService -> Verifying stock and updating...");
        Thread.Sleep(SimulatedDelays.DbWriteInsertUpdate);

        var products = _store.ReadAll<Product>(DataFiles.Products);
        var productMap = products.ToDictionary(p => p.Id);

        foreach (var item in order.Items)
        {
            if (!productMap.TryGetValue(item.ProductId, out var product) || product.StockQuantity < item.Quantity)
            {
                Log.Info($"SyncInventoryService -> Insufficient stock for Product #{item.ProductId} (Order #{order.Id}). Rolling back...");
                Thread.Sleep(SimulatedDelays.DbWriteCommit);
                return false;
            }
        }

        foreach (var item in order.Items)
        {
            productMap[item.ProductId].StockQuantity -= item.Quantity;
        }

        _store.WriteAll(DataFiles.Products, products);

        Log.Info($"SyncInventoryService -> Committing transaction...");
        Thread.Sleep(SimulatedDelays.DbWriteCommit);

        Log.Success($"SyncInventoryService -> Stock successfully reserved for Order #{order.Id}.");
        return true;
    }

    public void ReleaseStock(Order order)
    {
        Log.Info($"SyncInventoryService -> Releasing stock for Order #{order.Id}...");

        Thread.Sleep(SimulatedDelays.DbWriteOpenConnection);
        Thread.Sleep(SimulatedDelays.DbWriteBeginTransaction);
        Thread.Sleep(SimulatedDelays.DbWriteInsertUpdate);

        var products = _store.ReadAll<Product>(DataFiles.Products);
        var productMap = products.ToDictionary(p => p.Id);

        foreach (var item in order.Items)
        {
            if (productMap.TryGetValue(item.ProductId, out var product))
            {
                product.StockQuantity += item.Quantity;
            }
        }

        _store.WriteAll(DataFiles.Products, products);

        Thread.Sleep(SimulatedDelays.DbWriteCommit);
        Log.Info($"SyncInventoryService -> Stock released for Order #{order.Id}.");
    }
}
