using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Repositories;

public class CouponRepository
{
    private readonly JsonFileStore _store;

    public CouponRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public async Task<Coupon?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        Log.Info("CouponRepository -> Opening connection...");
        await Task.Delay(SimulatedDelays.DbQueryOpenConnection, ct).ConfigureAwait(false);

        Log.Info($"CouponRepository -> Executing query for Coupon '{code}'...");
        await Task.Delay(SimulatedDelays.DbQueryExecute(), ct).ConfigureAwait(false);

        var coupons = await _store.ReadAllAsync<Coupon>(DataFiles.Coupons, ct).ConfigureAwait(false);
        var coupon = coupons.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

        if (coupon != null)
        {
            Log.Info($"CouponRepository -> Coupon '{code}' found ({coupon.DiscountPercent}%)");
        }
        else
        {
            Log.Info($"CouponRepository -> Coupon '{code}' not found");
        }

        return coupon;
    }
}
