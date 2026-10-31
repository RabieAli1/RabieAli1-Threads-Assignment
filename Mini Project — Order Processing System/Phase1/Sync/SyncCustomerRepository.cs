using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Sync;

public class SyncCustomerRepository
{
    private readonly JsonFileStore _store;

    public SyncCustomerRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public Customer? GetById(int id)
    {
        Log.Info("SyncCustomerRepository -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);

        Log.Info($"SyncCustomerRepository -> Executing query for Customer #{id}...");
        Thread.Sleep(SimulatedDelays.DbQueryExecute());

        var customers = _store.ReadAll<Customer>(DataFiles.Customers);
        var customer = customers.FirstOrDefault(c => c.Id == id);

        if (customer != null)
        {
            Log.Info($"SyncCustomerRepository -> Customer #{id} found ({customer.Name})");
        }
        else
        {
            Log.Info($"SyncCustomerRepository -> Customer #{id} not found");
        }

        return customer;
    }
}
