using OrderProcessing.Phase1.Repositories;
using OrderProcessing.Shared.Exceptions;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase3;

public class CustomerService
{
    private readonly CustomerRepository _customerRepository;

    public CustomerService(CustomerRepository? customerRepository = null, JsonFileStore? store = null)
    {
        _customerRepository = customerRepository ?? new CustomerRepository(store);
    }

    /// <summary>
    /// Loads customer profile via DB query (with simulated delays).
    /// Throws CustomerNotFoundException if the customer does not exist.
    /// </summary>
    public async Task<Customer> GetCustomerAsync(int id, CancellationToken ct = default)
    {
        Log.Info($"CustomerService -> Fetching Customer #{id}...");
        var customer = await _customerRepository.GetByIdAsync(id, ct).ConfigureAwait(false);

        if (customer == null)
        {
            Log.Error($"CustomerService -> Customer #{id} not found!");
            throw new CustomerNotFoundException($"Customer #{id} not found");
        }

        return customer;
    }
}
