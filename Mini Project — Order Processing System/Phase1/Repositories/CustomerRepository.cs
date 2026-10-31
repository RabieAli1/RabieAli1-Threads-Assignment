using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Repositories;

public class CustomerRepository
{
    private readonly JsonFileStore _store;

    public CustomerRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public async Task<Customer?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        Log.Info("CustomerRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"CustomerRepository -> Executing query for Customer #{id}...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var customers = await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct).ConfigureAwait(false);
        var customer = customers.FirstOrDefault(c => c.Id == id);

        if (customer != null)
        {
            Log.Info($"CustomerRepository -> Customer #{id} found ({customer.Name})");
        }
        else
        {
            Log.Info($"CustomerRepository -> Customer #{id} not found");
        }

        return customer;
    }
}
