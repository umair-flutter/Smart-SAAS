using System.Globalization;
using SmartSchool.Shared.Enums;

namespace SmartSchool.Client.Services;

public static class Format
{
    public static string Money(decimal amount, string currency) =>
        $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    public static string Date(DateTime? utc) => utc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "—";

    public static string InvoiceBadge(InvoiceStatus status, bool overdue) => overdue ? "bg-red-lt" : status switch
    {
        InvoiceStatus.Paid => "bg-green-lt",
        InvoiceStatus.PartiallyPaid => "bg-yellow-lt",
        InvoiceStatus.Issued => "bg-blue-lt",
        InvoiceStatus.Cancelled => "bg-secondary-lt",
        _ => "bg-secondary-lt"
    };

    public static string InvoiceLabel(InvoiceStatus status, bool overdue) => overdue ? "Overdue" : status.DisplayName();
}
