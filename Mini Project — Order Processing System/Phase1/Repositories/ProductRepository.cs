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

    /// <summary>
    /// Thread-safe stock reservation protected by SemaphoreSlim.
    /// Prevents race conditions and overselling.
    /// </summary>
    public async Task<bool> ReserveStockAsync(int productId, int qty, CancellationToken ct = default)
    {
        await StockLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Log.Info("ProductRepository -> Opening connection...");
            await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

            Log.Info("ProductRepository -> Beginning transaction...");
            await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

            Log.Info($"ProductRepository -> Checking and updating stock for Product #{productId} (-{qty})...");
            await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var product = products.FirstOrDefault(p => p.Id == productId);

            if (product == null || product.StockQuantity < qty)
            {
                Log.Info($"ProductRepository -> Reserve failed for Product #{productId}: insufficient stock ({product?.StockQuantity ?? 0} < {qty})");
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

    /// <summary>
    /// Thread-safe stock release protected by SemaphoreSlim.
    /// </summary>
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
            Log.Success($"ProductRepository -> Stock released for Product #{productId}. Remaining: {product?.StockQuantity ?? 0}");
        }
        finally
        {
            StockLock.Release();
        }
    }

    /// <summary>
    /// Sets stock quantity directly (used by RaceConditionDemo).
    /// </summary>
    public async Task SetStockAsync(int productId, int stock, CancellationToken ct = default)
    {
        await StockLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
            var product = products.FirstOrDefault(p => p.Id == productId);
            if (product != null)
            {
                product.StockQuantity = stock;
                await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            StockLock.Release();
        }
    }

    /// <summary>
    /// Unsafe reservation used specifically for the Race Condition demonstration in Task 8.3.
    /// Requirement 1: read the stock, wait (open 200, begin transaction 100, update 400), then write the new stock - with NO protection.
    /// </summary>
    public async Task<bool> ReserveStockUnsafeAsync(int productId, int qty, CancellationToken ct = default)
    {
        // 1. Read stock with no lock
        var products = await _store.ReadAllAsync<Product>(DataFiles.Products, ct).ConfigureAwait(false);
        var product = products.FirstOrDefault(p => p.Id == productId);

        if (product == null || product.StockQuantity < qty)
        {
            Log.Info($"ProductRepository [UNSAFE] -> Insufficient stock for Product #{productId}");
            return false;
        }

        // 2. Wait: open 200, begin transaction 100, update 400
        Log.Info($"ProductRepository [UNSAFE] -> Read stock={product.StockQuantity}. Opening connection (200ms)...");
        await Task.Delay(200, ct).ConfigureAwait(false);

        Log.Info("ProductRepository [UNSAFE] -> Beginning transaction (100ms)...");
        await Task.Delay(100, ct).ConfigureAwait(false);

        Log.Info($"ProductRepository [UNSAFE] -> Updating stock for Product #{productId} (400ms)...");
        await Task.Delay(400, ct).ConfigureAwait(false);

        // 3. Write new stock with no protection
        product.StockQuantity -= qty;
        await _store.WriteAllAsync(DataFiles.Products, products, ct).ConfigureAwait(false);

        await Task.Delay(200, ct).ConfigureAwait(false); // commit
        Log.Success($"ProductRepository [UNSAFE] -> Reserved 1 unit for Product #{productId}. Wrote new stock={product.StockQuantity}");

        return true;
    }
}
