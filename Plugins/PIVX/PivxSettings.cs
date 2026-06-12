using System;

namespace BTCPayServer.Plugins.PIVX;

public class PivxSettings
{
    public string? DaemonUri { get; set; }     // e.g. http://127.0.0.1:51473
    public string? RpcUser { get; set; }
    public string? RpcPassword { get; set; }

    // pivx-walletd endpoint, needed for shielded watch-only stores
    public string? WalletdUri { get; set; }

    // Optional floor on confirmations before a payment settles.
    // 0 = follow the store's transaction speed policy.
    public int MinConfirmations { get; set; }

    public void NormalizeFromEnv()
    {
        string? E(string k) => Environment.GetEnvironmentVariable($"BTCPAY_PIVX_{k}");
        DaemonUri   ??= E("DAEMON_URI");
        RpcUser     ??= E("RPCUSER");
        RpcPassword ??= E("RPCPASSWORD");
        WalletdUri  ??= E("WALLETD_URI");

        if (int.TryParse(E("MINCONF") ?? "", out var c)) MinConfirmations = c;
    }
}