using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Repositories;

public class ProductRepository
{
    private readonly JsonFileStore _store;
    private static readonly SemaphoreSlim StockLock = new(1, 1);

    public ProductRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public async Task<Product?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        Log.Info("ProductRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"ProductRepository -> Executing query for Product #{id}...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
        var product = products.FirstOrDefault(p => p.Id == id);

        if (product != null)
        {
            Log.Info($"ProductRepository -> Product #{id} found ({product.Name})");
        }
        else
        {
            Log.Info($"ProductRepository -> Product #{id} not found");
        }

        return product;
    }

    public async Task<List<Product>> GetAllAsync(CancellationToken ct = default)
    {
        Log.Info("ProductRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info("ProductRepository -> Executing query for all products...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        return await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
    }

    public async Task<bool> ReserveStockAsync(int productId, int qty, CancellationToken ct = default)
    {
        await StockLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log.Info("ProductRepository -> Opening connection...");
            await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

            Log.Info("ProductRepository -> Beginning transaction...");
            await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

            Log.Info($"ProductRepository -> Updating stock for Product #{productId} (-{qty})...");
            await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var product = products.FirstOrDefault(p => p.Id == productId);

            if (product == null || product.StockQuantity < qty)
            {
                Log.Info($"ProductRepository -> Reserve failed for Product #{productId}: insufficient stock or not found");
                return false;
            }

            product.StockQuantity -= qty;
            await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);

            Log.Info("ProductRepository -> Committing transaction...");
            await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);
            Log.Success($"ProductRepository -> Stock reserved for Product #{productId}. Remaining: {product.StockQuantity}");

            return true;
        }
        finally
        {
            StockLock.Release();
        }
    }

    public async Task ReleaseStockAsync(int productId, int qty, CancellationToken ct = default)
    {
        await StockLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log.Info("ProductRepository -> Opening connection...");
            await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

            Log.Info("ProductRepository -> Beginning transaction...");
            await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

            Log.Info($"ProductRepository -> Releasing stock for Product #{productId} (+{qty})...");
            await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var product = products.FirstOrDefault(p => p.Id == productId);

            if (product != null)
            {
                product.StockQuantity += qty;
                await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);
            }

            Log.Info("ProductRepository -> Committing transaction...");
            await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);
            Log.Success($"ProductRepository -> Stock released for Product #{productId}.");
        }
        finally
        {
            StockLock.Release();
        }
    }

    /// <summary>
    /// Unsafe reservation used specifically for the Race Condition demonstration in Phase 2 Task 8.3.
    /// Reads stock, waits delays without locking, and overwrites stock.
    /// </summary>
    public async Task<bool> ReserveStockUnsafeAsync(int productId, int qty, CancellationToken ct = default)
    {
        Log.Info("ProductRepository [UNSAFE] -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

        Log.Info("ProductRepository [UNSAFE] -> Beginning transaction...");
        await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
        var product = products.FirstOrDefault(p => p.Id == productId);

        Log.Info($"ProductRepository [UNSAFE] -> Updating stock for Product #{productId}...");
        await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

        if (product == null || product.StockQuantity < qty)
        {
            Log.Info($"ProductRepository [UNSAFE] -> Insufficient stock for Product #{productId}");
            return false;
        }

        product.StockQuantity -= qty;
        await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);

        Log.Info("ProductRepository [UNSAFE] -> Committing transaction...");
        await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);

        return true;
    }
}

