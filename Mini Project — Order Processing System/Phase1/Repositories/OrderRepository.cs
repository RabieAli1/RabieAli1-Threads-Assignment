using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Repositories;

public class OrderRepository
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failSaveForOrderIds;

    public OrderRepository(JsonFileStore? store = null, IEnumerable<int>? failSaveForOrderIds = null)
    {
        _store = store ?? new JsonFileStore();
        _failSaveForOrderIds = failSaveForOrderIds != null ? new HashSet<int>(failSaveForOrderIds) : new HashSet<int>();
    }

    public async Task SaveAsync(Order order, CancellationToken ct = default)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbWriteOpenConnection, ct).ConfigureAwait(false);

        Log.Info("OrderRepository -> Beginning transaction...");
        await Task.Delay(SimulatedDelays.DbWriteBeginTransaction, ct).ConfigureAwait(false);

        Log.Info($"OrderRepository -> Inserting/Updating Order #{order.Id} (Status: {order.Status})...");
        await Task.Delay(SimulatedDelays.DbWriteInsertUpdate, ct).ConfigureAwait(false);

        // Simulated controlled failure for Phase 3 (Order #13 during completed commit)
        if (order.Status == OrderStatus.Completed && _failSaveForOrderIds.Contains(order.Id))
        {
            Log.Error($"OrderRepository -> Database error: Connection lost during commit for Order #{order.Id}");
            throw new DatabaseWriteException("Connection lost during commit");
        }

        var orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
        int index = orders.FindIndex(o => o.Id == order.Id);
        if (index >= 0)
        {
            orders[index] = order;
        }
        else
        {
            orders.Add(order);
        }

        await _store.WriteAllAsync(DataFiles.Orders, orders, ct).ConfigureAwait(false);

        Log.Info("OrderRepository -> Committing transaction...");
        await Task.Delay(SimulatedDelays.DbWriteCommit, ct).ConfigureAwait(false);
        Log.Success($"OrderRepository -> Order #{order.Id} saved successfully (Status: {order.Status})");
    }

    public async Task<List<Order>> GetAllAsync(CancellationToken ct = default)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info("OrderRepository -> Executing query for all orders...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        return await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus s, CancellationToken ct = default)
    {
        Log.Info("OrderRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"OrderRepository -> Executing query for orders with status '{s}'...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
        return orders.Where(o => o.Status == s).ToList();
    }
}
