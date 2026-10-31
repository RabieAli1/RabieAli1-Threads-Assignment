using OrderProcessing.Shared;
using OrderProcessing.Shared.Logging;
using OrderProcessing.Shared.Models;
using OrderProcessing.Shared.Storage;

namespace OrderProcessing.Phase1.Sync;

public class SyncCouponRepository
{
    private readonly JsonFileStore _store;

    public SyncCouponRepository(JsonFileStore? store = null)
    {
        _store = store ?? new JsonFileStore();
    }

    public Coupon? GetByCode(string code)
    {
        Log.Info("SyncCouponRepository -> Opening connection...");
        Thread.Sleep(SimulatedDelays.DbQueryOpenConnection);

        Log.Info($"SyncCouponRepository -> Executing query for Coupon '{code}'...");
        Thread.Sleep(SimulatedDelays.DbQueryExecute());

        var coupons = _store.ReadAll<Coupon>(DataFiles.Coupons);
        var coupon = coupons.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

        if (coupon != null)
        {
            Log.Info($"SyncCouponRepository -> Coupon '{code}' found ({coupon.DiscountPercent}%)");
        }
        else
        {
            Log.Info($"SyncCouponRepository -> Coupon '{code}' not found");
        }

        return coupon;
    }
}
