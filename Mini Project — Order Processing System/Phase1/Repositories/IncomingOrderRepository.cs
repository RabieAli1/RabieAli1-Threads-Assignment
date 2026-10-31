using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Repositories;

public class IncomingOrderRepository
{
    private readonly JsonFileStore _store;

    public IncomingOrderRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken ct = default)
    {
        Log.Info("IncomingOrderRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info("IncomingOrderRepository -> Executing query for all incoming orders...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var orders = await _store.ReadAllAsync<Order>(DataFiles.IncomingOrders, ct).ConfigureAwait(false);
        Log.Info($"IncomingOrderRepository -> Loaded {orders.Count} incoming orders");
        return orders;
    }
}

