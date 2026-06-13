using System;
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
}
