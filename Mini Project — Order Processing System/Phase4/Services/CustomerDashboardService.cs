using System.Diagnostics;
using OrderProcessing.Phase4.Models;
using OrderProcessing.Shared;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase4.Services;

public class CustomerDashboardService
{
    private readonly JsonFileStore _store;
    private readonly HashSet<int> _failPaymentsForCustomerIds;

    public CustomerDashboardService(
        IEnumerable<int>? failPaymentsForCustomerIds = null,
        JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
        _failPaymentsForCustomerIds = failPaymentsForCustomerIds != null
            ? new HashSet<int>(failPaymentsForCustomerIds)
            : new HashSet<int>();
    }

    private async Task SimulateDbQueryAsync(string queryName, CancellationToken ct)
    {
        Log.Info($"[Dashboard] Opening connection for {queryName}...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"[Dashboard] Executing DB query for {queryName}...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);
    }

    public async Task<CustomerDashboard> LoadAsync(int customerId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        Log.Info($"[Dashboard] Loading dashboard for Customer #{customerId} (4 concurrent queries)...");

        // Load 1: Profile
        var customerTask = Task.Run(async () =>
        {
            await SimulateDbQueryAsync($"Customer #{customerId} Profile", ct).ConfigureAwait(false);
            var customers = await _store.ReadAllAsync<Customer>(DataFiles.Customers, ct).ConfigureAwait(false);
            var customer = customers.FirstOrDefault(c => c.Id == customerId);
            if (customer == null)
            {
                throw new CustomerNotFoundException($"Customer #{customerId} not found");
            }
            return customer;
        }, ct);

        // Load 2: Orders
        var ordersTask = Task.Run(async () =>
        {
            await SimulateDbQueryAsync($"Customer #{customerId} Orders", ct).ConfigureAwait(false);
            var orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
            return orders.Where(o => o.CustomerId == customerId).ToList();
        }, ct);

        // Load 3: Payments
        var paymentsTask = Task.Run(async () =>
        {
            await SimulateDbQueryAsync($"Customer #{customerId} Payments", ct).ConfigureAwait(false);

            // R5: Failure injection
            if (_failPaymentsForCustomerIds.Contains(customerId))
            {
                throw new TimeoutException("Payments store timeout");
            }

            // Customer orders to match payment transactions
            var orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
            var customerOrderIds = orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();

            var payments = await _store.ReadAllAsync<PaymentTransaction>(DataFiles.Payments, ct).ConfigureAwait(false);
            return payments.Where(p => customerOrderIds.Contains(p.OrderId)).ToList();
        }, ct);

        // Load 4: Emails
        var emailsTask = Task.Run(async () =>
        {
            await SimulateDbQueryAsync($"Customer #{customerId} Emails", ct).ConfigureAwait(false);
            var orders = await _store.ReadAllAsync<Order>(DataFiles.Orders, ct).ConfigureAwait(false);
            var customerOrderIds = orders.Where(o => o.CustomerId == customerId).Select(o => o.Id).ToHashSet();

            var emails = await _store.ReadAllAsync<EmailMessage>(DataFiles.Emails, ct).ConfigureAwait(false);
            return emails.Where(e => customerOrderIds.Contains(e.OrderId)).ToList();
        }, ct);

        // R2: The four loads run at the same time, and the method waits for all of them with a single await.
        var allLoadsTask = Task.WhenAll(customerTask, ordersTask, paymentsTask, emailsTask);

        try
        {
            await allLoadsTask.ConfigureAwait(false);
        }
        catch
        {
            // R4: If more than one load fails, the method reports every failure, not only the first one
            var aggregate = allLoadsTask.Exception?.Flatten();
            if (aggregate != null)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Log.Error($"[Dashboard] Error loading dashboard for Customer #{customerId}: {inner.Message}");
                }
                throw aggregate;
            }
            throw;
        }

        sw.Stop();
        Log.Success($"[Dashboard] Loaded dashboard for Customer #{customerId} in {sw.ElapsedMilliseconds} ms (slowest single query)");

        return new CustomerDashboard
        {
            Customer = await customerTask.ConfigureAwait(false),
            Orders = await ordersTask.ConfigureAwait(false),
            Payments = await paymentsTask.ConfigureAwait(false),
            Emails = await emailsTask.ConfigureAwait(false)
        };
    }
}

