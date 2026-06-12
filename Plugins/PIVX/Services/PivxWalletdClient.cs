using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.PIVX.Services;

/// <summary>
/// Client for pivx-walletd, which derives diversified Sapling addresses
/// from an extended full viewing key. https://github.com/Liquid369/pivx-walletd
/// </summary>
public class PivxWalletdClient
{
    private readonly HttpClient _http;
    private readonly PivxSettings _cfg;

    public PivxWalletdClient(HttpClient http, IOptions<PivxSettings> cfg)
    {
        _http = http;
        _cfg = cfg.Value;
    }

    public bool Configured => !string.IsNullOrWhiteSpace(_cfg.WalletdUri);

    private record DeriveResult(string? address, long index, string? error);

    public async Task<(string Address, long Index)> DeriveAsync(string fvk, long index, CancellationToken ct = default)
    {
        if (!Configured)
            throw new InvalidOperationException("pivx-walletd is not configured. Set BTCPAY_PIVX_WALLETD_URI.");

        var baseUri = new Uri(_cfg.WalletdUri!.TrimEnd('/') + "/");
        // fully qualified: NBitpayClient ships a conflicting PostAsJsonAsync extension
        using var resp = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(
            _http, new Uri(baseUri, "derive"), new { fvk, index }, ct);
        var result = await resp.Content.ReadFromJsonAsync<DeriveResult>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode || result?.address is null)
            throw new InvalidOperationException($"pivx-walletd derive failed: {result?.error ?? resp.StatusCode.ToString()}");
        return (result.address, result.index);
    }

    public async Task<bool> HealthyAsync(CancellationToken ct = default)
    {
        if (!Configured)
            return false;
        try
        {
            var baseUri = new Uri(_cfg.WalletdUri!.TrimEnd('/') + "/");
            using var resp = await _http.GetAsync(new Uri(baseUri, "health"), ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
