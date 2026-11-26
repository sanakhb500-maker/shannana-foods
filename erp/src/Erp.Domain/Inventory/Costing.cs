namespace Erp.Domain.Inventory;

public static class Costing
{
    public const int Decimals = 4;

    /// <summary>
    /// Moving-average unit cost after receiving <paramref name="inQty"/> at <paramref name="inCost"/>
    /// on top of <paramref name="oldQty"/> at <paramref name="oldAvg"/>.
    /// With no positive stock on hand the cost simply restarts at the incoming cost.
    /// </summary>
    public static decimal MovingAverage(decimal oldQty, decimal oldAvg, decimal inQty, decimal inCost)
    {
        if (inQty <= 0) throw new ArgumentOutOfRangeException(nameof(inQty), "Incoming quantity must be positive.");
        var newQty = oldQty + inQty;
        if (oldQty <= 0 || newQty <= 0) return Math.Round(inCost, Decimals, MidpointRounding.AwayFromZero);
        return Math.Round((oldQty * oldAvg + inQty * inCost) / newQty, Decimals, MidpointRounding.AwayFromZero);
    }
}
