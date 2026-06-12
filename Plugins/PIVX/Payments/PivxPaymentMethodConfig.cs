namespace BTCPayServer.Plugins.PIVX.Payments
{
    public class PivxPaymentMethodConfig
    {
        /// <summary>
        /// When enabled, invoices receive a Sapling shielded address (getnewshieldaddress)
        /// instead of a transparent one.
        /// </summary>
        public bool UseShieldedAddresses { get; set; }
    }
}
