using OrderProcessing.Shared.Models;

namespace OrderProcessing.Phase2;

/// <summary>
/// Exact pricing rules according to Task 8.1.
/// All monetary values are rounded using Math.Round(x, 2, MidpointRounding.AwayFromZero).
/// </summary>
public static class PricingRules
{
    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static int GetLoyaltyDiscountPercent(LoyaltyTier tier) => tier switch
    {
        LoyaltyTier.Bronze => 0,
        LoyaltyTier.Silver => 5,
        LoyaltyTier.Gold => 10,
        _ => 0
    };

    /// <summary>
    /// loyalty (Bronze 0, Silver 5, Gold 10) + coupon % (0 if none); capped at 30.
    /// </summary>
    public static int CalculateDiscountPercent(LoyaltyTier tier, int couponPercent)
    {
        int total = GetLoyaltyDiscountPercent(tier) + couponPercent;
        return Math.Min(30, Math.Max(0, total));
    }

    /// <summary>
    /// SubTotal * Discount % / 100
    /// </summary>
    public static decimal CalculateDiscount(decimal subTotal, int discountPercent)
    {
        decimal discount = subTotal * discountPercent / 100m;
        return RoundMoney(discount);
    }

    /// <summary>
    /// 5.00 + 1.50 * total quantity + 2.00 * total weight (kg); 0 when SubTotal >= 1000
    /// </summary>
    public static decimal CalculateShippingCost(decimal subTotal, int totalQuantity, decimal totalWeightKg)
    {
        if (subTotal >= 1000m)
        {
            return 0.00m;
        }

        decimal shipping = 5.00m + (1.50m * totalQuantity) + (2.00m * totalWeightKg);
        return RoundMoney(shipping);
    }

    /// <summary>
    /// SubTotal - Discount + ShippingCost
    /// </summary>
    public static decimal CalculateTotalAmount(decimal subTotal, decimal discount, decimal shippingCost)
    {
        return RoundMoney(subTotal - discount + shippingCost);
    }
}

