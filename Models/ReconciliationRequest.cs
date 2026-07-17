namespace QrLedgerReconciler.Models;

public sealed record ReconciliationRequest(
    string UserName,
    string Password,
    DateTime From,
    DateTime To);
