using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class InventoryService
{
    private readonly JsonFileStore _store;
    private static readonly SemaphoreSlim InventoryLock = new(1, 1);

    public InventoryService(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    /// <summary>
    /// All items or nothing safe reservation. Rolls back completely on failure.
    /// DB write: Open connection 200 -> Begin transaction 100 -> Insert/Update 400 -> Commit 200.
    /// </summary>
    public async Task<bool> ReserveStockAsync(Order order, CancellationToken ct = default)
    {
        await InventoryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log.Info($"InventoryService -> Opening connection for Order #{order.Id} stock reservation...");
            await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Beginning transaction for Order #{order.Id}...");
            await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Checking all items stock for Order #{order.Id}...");
            await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var productMap = products.ToDictionary(p => p.Id);

            // 1. Verify that all items have sufficient stock
            foreach (var item in order.Items)
            {
                if (!productMap.TryGetValue(item.ProductId, out var product) || product.StockQuantity < item.Quantity)
                {
                    string name = product?.Name ?? $"Product #{item.ProductId}";
                    Log.Info($"InventoryService -> Insufficient stock for '{name}' (needed {item.Quantity}, available {product?.StockQuantity ?? 0}). Rolling back.");
                    return false;
                }
            }

            // 2. All items available: deduct stock for all items
            foreach (var item in order.Items)
            {
                productMap[item.ProductId].StockQuantity -= item.Quantity;
            }

            await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Committing transaction for Order #{order.Id}...");
            await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);

            Log.Success($"InventoryService -> All items reserved successfully for Order #{order.Id}.");
            return true;
        }
        finally
        {
            InventoryLock.Release();
        }
    }

    /// <summary>
    /// Adds every item's quantity back to inventory.
    /// DB write: Open connection 200 -> Begin transaction 100 -> Insert/Update 400 -> Commit 200.
    /// </summary>
    public async Task ReleaseStockAsync(Order order, CancellationToken ct = default)
    {
        await InventoryLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log.Info($"InventoryService -> Opening connection for Order #{order.Id} stock release...");
            await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Beginning transaction for Order #{order.Id}...");
            await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Releasing items for Order #{order.Id}...");
            await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var productMap = products.ToDictionary(p => p.Id);

            foreach (var item in order.Items)
            {
                if (productMap.TryGetValue(item.ProductId, out var product))
                {
                    product.StockQuantity += item.Quantity;
                }
            }

            await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);

            Log.Info($"InventoryService -> Committing transaction for Order #{order.Id} stock release...");
            await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);

            Log.Success($"InventoryService -> Released all items for Order #{order.Id}.");
        }
        finally
        {
            InventoryLock.Release();
        }
    }
}
