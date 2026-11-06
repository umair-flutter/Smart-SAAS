namespace SmartSchool.Shared.Enums;

public enum BillingCycle
{
    Monthly = 1,
    Yearly = 2
}

public enum InvoiceStatus
{
    Draft = 1,
    Issued = 2,
    PartiallyPaid = 3,
    Paid = 4,
    Cancelled = 5
}

public enum PaymentMethod
{
    BankTransfer = 1,
    Card = 2,
    Cash = 3,
    Cheque = 4,
    Other = 5
}

public static class BillingEnumExtensions
{
    public static string DisplayName(this InvoiceStatus s) => s switch
    {
        InvoiceStatus.PartiallyPaid => "Partially paid",
        _ => s.ToString()
    };

    public static string DisplayName(this PaymentMethod m) => m switch
    {
        PaymentMethod.BankTransfer => "Bank transfer",
        _ => m.ToString()
    };

    /// <summary>Invoices that still expect money.</summary>
    public static bool IsOpen(this InvoiceStatus s) => s is InvoiceStatus.Issued or InvoiceStatus.PartiallyPaid;
}
