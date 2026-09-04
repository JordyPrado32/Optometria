namespace OptometriaApp.Services;

public static class PurchasePaymentRules
{
    public static void ValidateTotals(decimal subtotal, decimal discount, decimal tax, decimal paid)
    {
        if (new[] { subtotal, discount, tax, paid }.Any(x => x < 0 || x > 9999999999999.99m || decimal.Round(x, 2) != x)
            || discount > subtotal || subtotal - discount + tax < paid)
            throw new InvalidOperationException("Revisa los importes: usa valores no negativos con dos decimales, descuento hasta el subtotal y total no menor al saldo pagado.");
    }

    public static void ValidatePayment(decimal total, decimal paid, decimal amount)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > total - paid)
            throw new InvalidOperationException("El abono debe ser positivo, tener hasta dos decimales y no superar el saldo pendiente.");
    }

    public static string State(decimal total, decimal paid) => paid >= total ? "Pagada" : paid > 0 ? "Parcial" : "Pendiente";
}
