using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.PIVX.Services
{
    public class PivxSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly PivxRpcClient _rpcClient;
        private readonly ILogger<PivxSyncSummaryProvider> _logger;
        private PivxSyncStatus? _cachedStatus;
        private DateTime _lastUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheTime = TimeSpan.FromSeconds(30);

        public PivxSyncSummaryProvider(PivxRpcClient rpcClient, ILogger<PivxSyncSummaryProvider> logger)
        {
            _rpcClient = rpcClient;
            _logger = logger;
        }

        public bool AllAvailable()
        {
            return GetCurrentStatus().Available;
        }

        public string Partial { get; } = "PIVX/PivxSyncSummary";
        
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            yield return GetCurrentStatus();
        }

        private PivxSyncStatus GetCurrentStatus()
        {
            if (_cachedStatus != null && DateTime.UtcNow - _lastUpdate < _cacheTime)
            {
                return _cachedStatus;
            }

            try
            {
                var blockchainInfo = _rpcClient.GetBlockchainInfoAsync(CancellationToken.None).GetAwaiter().GetResult();
                
                _cachedStatus = new PivxSyncStatus
                {
                    PaymentMethodId = "PIVX",
                    CurrentHeight = blockchainInfo.blocks,
                    TargetHeight = blockchainInfo.headers,
                    Synced = blockchainInfo.blocks >= blockchainInfo.headers && blockchainInfo.blocks > 0
                };
                _cachedStatus.SetAvailable(true);
                _lastUpdate = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get PIVX sync status");
                _cachedStatus = new PivxSyncStatus
                {
                    PaymentMethodId = "PIVX",
                    Synced = false
                };
                _cachedStatus.SetAvailable(false);
            }

            return _cachedStatus;
        }
    }

    public class PivxSyncStatus : SyncStatus, ISyncStatus
    {
        public new string PaymentMethodId { get; set; } = "PIVX";
        public long CurrentHeight { get; set; }
        public long TargetHeight { get; set; }
        public bool Synced { get; set; }
        private bool _available;

        public override bool Available => _available;
        
        public void SetAvailable(bool value) => _available = value;
    }
}

