using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.PIVX.Services;

/// <summary>
/// Cached daemon wallet balance for the dashboard widget. The wallet is
/// shared by every store on the instance, so the numbers are instance-wide.
/// </summary>
public class PivxBalanceProvider
{
    private readonly PivxRpcClient _rpc;
    private readonly ILogger<PivxBalanceProvider> _logger;
    private readonly TimeSpan _cacheTime = TimeSpan.FromSeconds(30);
    private Balance? _cached;
    private DateTime _lastUpdate = DateTime.MinValue;

    public PivxBalanceProvider(PivxRpcClient rpc, ILogger<PivxBalanceProvider> logger)
    {
        _rpc = rpc;
        _logger = logger;
    }

    public record Balance(bool Available, decimal Total, decimal Shield)
    {
        public decimal Transparent => Total - Shield;
    }

    public Balance GetBalance()
    {
        if (_cached != null && DateTime.UtcNow - _lastUpdate < _cacheTime)
            return _cached;

        try
        {
            var total = _rpc.GetBalanceAsync(0, true, true, true, CancellationToken.None).GetAwaiter().GetResult();
            decimal shield;
            try
            {
                shield = _rpc.GetShieldBalanceAsync("*", 0, true, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                // wallets without any shield notes can reject the call
                shield = 0m;
            }
            _cached = new Balance(true, total, shield);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch PIVX wallet balance");
            _cached = new Balance(false, 0m, 0m);
        }

        _lastUpdate = DateTime.UtcNow;
        return _cached;
    }

    public record Histogram(IReadOnlyList<string> Labels, IReadOnlyList<decimal> Series, decimal Balance);

    private readonly ConcurrentDictionary<string, (DateTime At, Histogram? Data)> _histograms = new();
    private static readonly TimeSpan HistogramCacheTime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Transparent balance over time, reconstructed by walking the wallet's
    /// transaction list backwards from the current balance. Shield balance has
    /// no spend history available over RPC, so the chart covers transparent
    /// funds only; the headline number still includes shield.
    /// </summary>
    public Histogram? GetHistogram(string type)
    {
        var days = type switch { "year" => 365, "month" => 30, _ => 7 };
        if (_histograms.TryGetValue(type, out var hit) && DateTime.UtcNow - hit.At < HistogramCacheTime)
            return hit.Data;

        Histogram? result = null;
        try
        {
            var transparent = _rpc.GetBalanceAsync(0, true, true, false, CancellationToken.None).GetAwaiter().GetResult();
            var txs = _rpc.ListTransactionsAsync(10000, 0, true, CancellationToken.None).GetAwaiter().GetResult();

            // newest first; each entry's net effect on the transparent balance
            var deltas = txs
                .Where(t => t.confirmations >= 0)
                .Select(t => (t.time, Delta: t.amount + (t.fee ?? 0m)))
                .OrderByDescending(t => t.time)
                .ToList();

            const int points = 30;
            var now = DateTimeOffset.UtcNow;
            var start = now.AddDays(-days);
            var step = (now - start) / points;

            var labels = new string[points];
            var series = new decimal[points];
            var balance = transparent;
            var di = 0;
            for (var i = points - 1; i >= 0; i--)
            {
                var bucketTime = start + step * (i + 1);
                var cutoff = bucketTime.ToUnixTimeSeconds();
                while (di < deltas.Count && deltas[di].time > cutoff)
                {
                    balance -= deltas[di].Delta;
                    di++;
                }
                labels[i] = bucketTime.UtcDateTime.ToString("o");
                series[i] = Math.Max(balance, 0m);
            }

            result = new Histogram(labels, series, GetBalance().Total);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build PIVX balance histogram");
        }

        _histograms[type] = (DateTime.UtcNow, result);
        return result;
    }
}
