using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3.Sync;

public class SyncOrderRepository
{
    private readonly JsonFileStore _store;

    public SyncOrderRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public void Save(Order order)
    {
        Log.Info($"SyncOrderRepository -> Opening connection for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.DbWriteOpenConnection);

        Log.Info($"SyncOrderRepository -> Beginning transaction for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.DbWriteBeginTransaction);

        Log.Info($"SyncOrderRepository -> Updating Order #{order.Id} (Status: {order.Status})...");
        Thread.Sleep(SimulatedDelays.DbWriteInsertUpdate);

        var orders = _store.ReadAll<Order>(DataFiles.Orders);
        int index = orders.FindIndex(o => o.Id == order.Id);
        if (index >= 0)
        {
            orders[index] = order;
        }
        else
        {
            orders.Add(order);
        }

        _store.WriteAll(DataFiles.Orders, orders);

        Log.Info($"SyncOrderRepository -> Committing transaction for Order #{order.Id}...");
        Thread.Sleep(SimulatedDelays.DbWriteCommit);

        Log.Success($"SyncOrderRepository -> Order #{order.Id} saved successfully (Status: {order.Status})");
    }

    public List<Order> GetAll()
    {
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);
        Thread.Sleep(SimulatedDelays.DbQueryExecute());
        return _store.ReadAll<Order>(DataFiles.Orders);
    }
}
