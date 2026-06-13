using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using System.Net.Http;

namespace BTCPayServer.Plugins.PIVX;

public class PivxRpcClient
{
    private readonly HttpClient _http;
    private readonly PivxSettings _cfg;
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public PivxRpcClient(HttpClient http, IOptions<PivxSettings> cfg)
    {
        _http = http;
        _cfg = cfg.Value;
    }

    private record RpcEnv<T>(string? jsonrpc, string? id, T? result, RpcError? error);
    private record RpcError(int code, string message);

    private async Task<T> CallAsync<T>(string method, CancellationToken ct, params object[] args)
    {
        if (string.IsNullOrWhiteSpace(_cfg.DaemonUri))
            throw new InvalidOperationException("PIVX DaemonUri not configured.");

        var req = new { jsonrpc = "1.0", id = "pivx", method, @params = args };
        using var msg = new HttpRequestMessage(HttpMethod.Post, _cfg.DaemonUri);
        if (!string.IsNullOrEmpty(_cfg.RpcUser))
        {
            var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_cfg.RpcUser}:{_cfg.RpcPassword}"));
            msg.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
        }
        msg.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();

        using var s = await resp.Content.ReadAsStreamAsync(ct);
        var env = await JsonSerializer.DeserializeAsync<RpcEnv<T>>(s, _json, ct);
        if (env?.error is not null)
            throw new Exception($"PIVX RPC error {env.error.code}: {env.error.message}");
        return env!.result!;
    }

    // ---------- Sapling / SHIELD ----------
    public Task<string> GetNewShieldAddressAsync(CancellationToken ct = default)
        => CallAsync<string>("getnewshieldaddress", ct);

    public Task<string> ExportSaplingViewingKeyAsync(string shieldAddr, CancellationToken ct = default)
        => CallAsync<string>("exportsaplingviewingkey", ct, shieldAddr);

    public Task<object> ImportSaplingViewingKeyAsync(string vkey, bool rescanWhenNew = true, int? startHeight = null, CancellationToken ct = default)
    {
        var rescan = rescanWhenNew ? "whenkeyisnew" : "no";
        return startHeight is int h
            ? CallAsync<object>("importsaplingviewingkey", ct, vkey, rescan, h)
            : CallAsync<object>("importsaplingviewingkey", ct, vkey, rescan);
    }

    public Task<List<ShieldRecv>> ListReceivedByShieldAddressAsync(string shieldAddr, int minconf = 1, CancellationToken ct = default)
        => CallAsync<List<ShieldRecv>>("listreceivedbyshieldaddress", ct, shieldAddr, minconf);

    public Task<decimal> GetShieldBalanceAsync(string addressOrStar = "*", int minconf = 1, bool includeWatchOnly = true, CancellationToken ct = default)
        => CallAsync<decimal>("getshieldbalance", ct, addressOrStar, minconf, includeWatchOnly);

    // ---------- Transparent (still available) ----------
    public Task<string> GetNewAddressAsync(string label = "btcpay", CancellationToken ct = default)
        => CallAsync<string>("getnewaddress", ct, label);

    public Task<decimal> GetReceivedByAddressAsync(string addr, int minconf, CancellationToken ct = default)
        => CallAsync<decimal>("getreceivedbyaddress", ct, addr, minconf);

    // watchonly_config=2 includes watch-only outputs alongside regular ones
    public Task<List<UnspentOutput>> ListUnspentAsync(string addr, int minconf = 0, int maxconf = 9999999, CancellationToken ct = default)
        => CallAsync<List<UnspentOutput>>("listunspent", ct, minconf, maxconf, new[] { addr }, 2);

    public Task<WalletTransaction> GetTransactionAsync(string txid, CancellationToken ct = default)
        => CallAsync<WalletTransaction>("gettransaction", ct, txid, true);

    public Task<object?> ImportAddressAsync(string address, string label = "", bool rescan = false, CancellationToken ct = default)
        => CallAsync<object?>("importaddress", ct, address, label, rescan);

    public Task<decimal> GetBalanceAsync(int minconf = 0, bool includeWatchonly = true, bool includeDelegated = true, bool includeShield = true, CancellationToken ct = default)
        => CallAsync<decimal>("getbalance", ct, minconf, includeWatchonly, includeDelegated, includeShield);

    public Task<BlockchainInfo> GetBlockchainInfoAsync(CancellationToken ct = default)
        => CallAsync<BlockchainInfo>("getblockchaininfo", ct);

    // Models
    public record BlockchainInfo
    {
        public string chain { get; set; } = "";
        public long blocks { get; set; }
        public long headers { get; set; }
        public string bestblockhash { get; set; } = "";
        public double difficulty { get; set; }
        public long mediantime { get; set; }
        public double verificationprogress { get; set; }
        public bool initialblockdownload { get; set; }
    }

    public record UnspentOutput
    {
        public string txid { get; set; } = "";
        public int vout { get; set; }
        public string address { get; set; } = "";
        public decimal amount { get; set; }
        public int confirmations { get; set; }
        public bool spendable { get; set; }
    }

    public record WalletTransaction
    {
        public string txid { get; set; } = "";
        public decimal amount { get; set; }
        public long confirmations { get; set; }
        public string? blockhash { get; set; }
        public long? blocktime { get; set; }
        public long time { get; set; }
    }

    public record ShieldRecv
    {
        public string address { get; set; } = "";
        public string txid { get; set; } = "";
        public decimal amount { get; set; }
        public int confirmations { get; set; }
        public string? memo { get; set; }
        public int? blockheight { get; set; }
        public long? blocktime { get; set; }
        public int? outindex { get; set; }
        public bool? change { get; set; }
    }
}
