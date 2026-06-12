namespace BTCPayServer.Plugins.PIVX.Payments
{
    public enum PivxAddressSource
    {
        // Addresses come from the pivxd wallet (spending keys on the server)
        DaemonWallet = 0,
        // Addresses are derived from public key material; the server cannot spend
        WatchOnly = 1
    }

    public class PivxPaymentMethodConfig
    {
        public PivxAddressSource AddressSource { get; set; }

        // Invoices get Sapling shield addresses instead of transparent ones
        public bool UseShieldedAddresses { get; set; }

        // Watch-only, transparent: account xpub, derived non-hardened at 0/index
        public string? AccountXpub { get; set; }

        // Watch-only, shielded: Sapling extended full viewing key (pxviews1...)
        public string? SaplingViewingKey { get; set; }
    }
}
