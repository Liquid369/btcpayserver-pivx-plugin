using BTCPayServer.Payments;

namespace BTCPayServer.Plugins.PIVX;

public class PivxLikeSpecificBtcPayNetwork : BTCPayNetworkBase
{
    public int MaxTrackedConfirmation { get; set; } = 10;
    public string UriScheme { get; set; } = "pivx";

    public static PivxLikeSpecificBtcPayNetwork Instance { get; } = new()
    {
        CryptoCode = "PIVX",
        DisplayName = "PIVX",
        Divisibility = 8,
        DefaultRateRules = new[]
        {
            // PIVX/USDT is the most liquid pair (Binance, MEXC, XT, BitMart...)
            "PIVX_X = PIVX_USDT * USDT_X",
            "PIVX_USDT = binance(PIVX_USDT)"
        },
        // Served from the plugin assembly via EmbeddedFileProvider
        CryptoImagePath = "pivx.svg"
    };
}
