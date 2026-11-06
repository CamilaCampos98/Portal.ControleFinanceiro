namespace Portal.ControleFinanceiro.Services;

public static class PluggyTransactionCorrection
{
    private static readonly DateTime SamPurchaseDate = new(2026, 11, 2);
    private static readonly DateTime SamFirstInstallment = new(2026, 11, 4);
    private static readonly DateTime SamLastInstallment = new(2027, 2, 28);

    public static PluggyCardTransaction? Apply(PluggyCardTransaction transaction)
    {
        if (!transaction.Description.Contains("SAM S TAMBOR", StringComparison.OrdinalIgnoreCase))
            return transaction;

        // Compra original estornada após o parcelamento, confirmada pela titular.
        if (transaction.Date.Date == SamPurchaseDate && transaction.Amount == 169.58m &&
            transaction.CardNumber.EndsWith("8930", StringComparison.Ordinal) &&
            transaction.TotalInstallments == 1)
            return null;

        // A Pluggy atribuiu as parcelas ao final 5169, mas a compra foi feita no 8930.
        if (transaction.Date.Date >= SamFirstInstallment && transaction.Date.Date <= SamLastInstallment &&
            transaction.Amount == 47.62m && transaction.TotalInstallments == 4 &&
            transaction.InstallmentNumber is >= 1 and <= 4 &&
            transaction.CardNumber.EndsWith("5169", StringComparison.Ordinal) &&
            (!transaction.PurchaseDate.HasValue ||
             transaction.PurchaseDate.Value.Date >= SamPurchaseDate &&
             transaction.PurchaseDate.Value.Date <= SamFirstInstallment))
            return transaction with { CardNumber = "8930", ReportedCardNumber = transaction.CardNumber };

        return transaction;
    }
}
