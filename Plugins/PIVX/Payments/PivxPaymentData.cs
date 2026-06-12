namespace BTCPayServer.Plugins.PIVX.Payments;

public class PivxPaymentData
{
    public string? TransactionId { get; set; }
    public int OutputIndex { get; set; }
    public string? Address { get; set; }
    public decimal Amount { get; set; }
    public long ConfirmationCount { get; set; }
    public bool IsShielded { get; set; }
}
