using System.Threading;
using BTCPayServer.Abstractions.Contracts;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.PIVX.Services;

/// <summary>
/// Hands out watch-only invoice addresses. Transparent addresses are derived
/// from the store's account xpub at 0/index and imported into pivxd as
/// watch-only; shielded addresses come from pivx-walletd. The per-store
/// derivation cursor is persisted through ISettingsRepository and advanced
/// under a lock, since invoices can be created concurrently.
/// </summary>
public class PivxWatchOnlyService
{
    private const byte PubkeyAddressPrefix = 30; // mainnet 'D'

    private readonly ISettingsRepository _settings;
    private readonly PivxRpcClient _rpc;
    private readonly PivxWalletdClient _walletd;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PivxWatchOnlyService(ISettingsRepository settings, PivxRpcClient rpc, PivxWalletdClient walletd)
    {
        _settings = settings;
        _rpc = rpc;
        _walletd = walletd;
    }

    public class Cursor
    {
        public long Next { get; set; }
    }

    private static string Key(string storeId, string kind) => $"PIVX_WATCHONLY_{kind}_{storeId}";

    /// <summary>
    /// Derives the transparent address at account/0/index from an xpub.
    /// Accepts any extended key version bytes (xpub, ToPKiB, ...), the
    /// 74-byte BIP32 payload is what matters.
    /// </summary>
    public static string DeriveTransparentAddress(string xpub, uint index)
    {
        var raw = Encoders.Base58Check.DecodeData(xpub.Trim());
        if (raw.Length != 78)
            throw new FormatException("Not an extended public key");
        var ext = new ExtPubKey(raw[4..]);
        var pubKey = ext.Derive(0).Derive(index).PubKey;
        var payload = new byte[21];
        payload[0] = PubkeyAddressPrefix;
        pubKey.Hash.ToBytes().CopyTo(payload, 1);
        return Encoders.Base58Check.EncodeData(payload);
    }

    public async Task<string> ReserveTransparentAddressAsync(string storeId, string xpub, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var key = Key(storeId, "T");
            var cursor = await _settings.GetSettingAsync<Cursor>(key) ?? new Cursor();
            var address = DeriveTransparentAddress(xpub, checked((uint)cursor.Next));
            await _rpc.ImportAddressAsync(address, $"btcpay:{storeId}", rescan: false, ct);
            cursor.Next++;
            await _settings.UpdateSetting(cursor, key);
            return address;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> ReserveShieldAddressAsync(string storeId, string viewingKey, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var key = Key(storeId, "S");
            var cursor = await _settings.GetSettingAsync<Cursor>(key) ?? new Cursor();
            var (address, usedIndex) = await _walletd.DeriveAsync(viewingKey, cursor.Next, ct);
            cursor.Next = usedIndex + 1;
            await _settings.UpdateSetting(cursor, key);
            return address;
        }
        finally
        {
            _lock.Release();
        }
    }
}
