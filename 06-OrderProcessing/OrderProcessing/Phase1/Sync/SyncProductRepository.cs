using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Sync;

public class SyncProductRepository
{
    private readonly JsonFileStore _store;

    public SyncProductRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public Product? GetById(int id)
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);

        Log.Info($"SyncProductRepository -> Executing query for Product #{id}...");
        Thread.Sleep(SimulatedDelays.DbQueryExecute());

        var products = _store.ReadAll<Product>(DataFiles.Products);
        var product = products.FirstOrDefault(p => p.Id == id);

        if (product != null)
        {
            Log.Info($"SyncProductRepository -> Product #{id} found ({product.Name})");
        }
        else
        {
            Log.Info($"SyncProductRepository -> Product #{id} not found");
        }

        return product;
    }

    public bool ReserveStock(int productId, int qty)
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbWriteOpenConnection);

        Log.Info("SyncProductRepository -> Beginning transaction...");
        Thread.Sleep(SimulatedDelays.DbWriteBeginTransaction);

        Log.Info($"SyncProductRepository -> Updating stock for Product #{productId} (-{qty})...");
        Thread.Sleep(SimulatedDelays.DbWriteInsertUpdate);

        var products = _store.ReadAll<Product>(DataFiles.Products);
        var product = products.FirstOrDefault(p => p.Id == productId);

        if (product == null || product.StockQuantity < qty)
        {
            Log.Info($"SyncProductRepository -> Reserve failed for Product #{productId}: insufficient stock or not found");
            return false;
        }

        product.StockQuantity -= qty;
        _store.WriteAll(DataFiles.Products, products);

        Log.Info("SyncProductRepository -> Committing transaction...");
        Thread.Sleep(SimulatedDelays.DbWriteCommit);
        Log.Success($"SyncProductRepository -> Stock reserved for Product #{productId}. Remaining: {product.StockQuantity}");

        return true;
    }

    public List<Product> GetAll()
    {
        Log.Info("SyncProductRepository -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);
        Log.Info("SyncProductRepository -> Executing query for all products...");
        Thread.Sleep(SimulatedDelays.DbQueryExecute());
        return _store.ReadAll<Product>(DataFiles.Products);
    }
}

