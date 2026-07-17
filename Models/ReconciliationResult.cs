namespace QrLedgerReconciler.Models;

public sealed record ReconciliationResult(decimal LedgerTotal, decimal QrTotal)
{
    public decimal Difference => QrTotal - LedgerTotal;
}
