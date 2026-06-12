using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PIVX.Payments;
using BTCPayServer.Plugins.PIVX.Services;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.PIVX.Controllers
{
    [Route("stores/{storeId}/pivx")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public class PivxStoreController : Controller
    {
        private readonly PivxRpcClient _pivxRpcClient;
        private readonly PaymentMethodHandlerDictionary _handlers;
        private readonly StoreRepository _storeRepository;
        private readonly ILogger<PivxStoreController> _logger;
        private IStringLocalizer StringLocalizer { get; }

        public PivxStoreController(
            PivxRpcClient pivxRpcClient,
            PaymentMethodHandlerDictionary handlers,
            StoreRepository storeRepository,
            ILogger<PivxStoreController> logger,
            IStringLocalizer stringLocalizer)
        {
            _pivxRpcClient = pivxRpcClient;
            _handlers = handlers;
            _storeRepository = storeRepository;
            _logger = logger;
            StringLocalizer = stringLocalizer;
        }

        private StoreData StoreData => HttpContext.GetStoreData();

        [HttpGet]
        public async Task<IActionResult> GetStorePivxPaymentMethod()
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("PIVX");
            var store = StoreData;
            var blob = store.GetStoreBlob();
            var excludeFilter = blob.GetExcludedPaymentMethods();
            
            var existingConfig = store.GetPaymentMethodConfig<PivxPaymentMethodConfig>(pmi, _handlers);
            
            // Test daemon connection
            var daemonAvailable = false;
            var blockchainInfo = "";
            try
            {
                var info = await _pivxRpcClient.GetBlockchainInfoAsync();
                if (info != null)
                {
                    daemonAvailable = true;
                    blockchainInfo = $"Chain: {info.chain}, Blocks: {info.blocks}, Difficulty: {info.difficulty:F2}";
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Failed to connect to PIVX daemon");
                blockchainInfo = $"Error: {ex.Message}";
            }

            var vm = new PivxPaymentMethodViewModel
            {
                CryptoCode = "PIVX",
                Enabled = existingConfig != null && !excludeFilter.Match(pmi),
                UseShieldedAddresses = existingConfig?.UseShieldedAddresses is true,
                WatchOnly = existingConfig?.AddressSource == PivxAddressSource.WatchOnly,
                AccountXpub = existingConfig?.AccountXpub ?? "",
                SaplingViewingKey = existingConfig?.SaplingViewingKey ?? "",
                DaemonAvailable = daemonAvailable,
                DaemonInfo = blockchainInfo
            };

            if (vm.WatchOnly && !vm.UseShieldedAddresses && !string.IsNullOrWhiteSpace(vm.AccountXpub))
            {
                try
                {
                    vm.FirstDerivedAddress = PivxWatchOnlyService.DeriveTransparentAddress(vm.AccountXpub, 0);
                }
                catch
                {
                    // shown as invalid on save instead
                }
            }

            return View("~/Views/Shared/PIVX/GetStorePivxPaymentMethod.cshtml", vm);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateStorePivxPaymentMethod(PivxPaymentMethodViewModel viewModel)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("PIVX");
            var store = StoreData;
            var blob = store.GetStoreBlob();

            if (viewModel.Enabled)
            {
                var config = new PivxPaymentMethodConfig
                {
                    UseShieldedAddresses = viewModel.UseShieldedAddresses,
                    AddressSource = viewModel.WatchOnly ? PivxAddressSource.WatchOnly : PivxAddressSource.DaemonWallet,
                    AccountXpub = string.IsNullOrWhiteSpace(viewModel.AccountXpub) ? null : viewModel.AccountXpub.Trim(),
                    SaplingViewingKey = string.IsNullOrWhiteSpace(viewModel.SaplingViewingKey) ? null : viewModel.SaplingViewingKey.Trim()
                };

                if (viewModel.WatchOnly && !viewModel.UseShieldedAddresses)
                {
                    try
                    {
                        PivxWatchOnlyService.DeriveTransparentAddress(config.AccountXpub ?? "", 0);
                    }
                    catch (System.Exception ex)
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Error,
                            Message = StringLocalizer["Invalid account xpub: {0}", ex.Message].Value
                        });
                        return RedirectToAction(nameof(GetStorePivxPaymentMethod), new { storeId = store.Id });
                    }
                }

                if (viewModel.WatchOnly && viewModel.UseShieldedAddresses)
                {
                    if (config.SaplingViewingKey?.StartsWith("pxview") is not true)
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel
                        {
                            Severity = StatusMessageModel.StatusSeverity.Error,
                            Message = StringLocalizer["A Sapling extended full viewing key (pxviews1...) is required for watch-only shielded payments"].Value
                        });
                        return RedirectToAction(nameof(GetStorePivxPaymentMethod), new { storeId = store.Id });
                    }

                    // Imports are idempotent; rescan runs in the daemon when the
                    // key is new and can take a while, so do it off-request.
                    var vkey = config.SaplingViewingKey;
                    var height = viewModel.ViewingKeyBirthHeight;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _pivxRpcClient.ImportSaplingViewingKeyAsync(vkey, true, height);
                            _logger.LogInformation("Imported sapling viewing key for store {StoreId}", store.Id);
                        }
                        catch (System.Exception ex)
                        {
                            _logger.LogError(ex, "Sapling viewing key import failed for store {StoreId}", store.Id);
                        }
                    });
                }

                _logger.LogInformation("Enabling PIVX payment method for store {StoreId}", store.Id);
                store.SetPaymentMethodConfig(_handlers[pmi], config);
                blob.SetExcluded(pmi, false);
            }
            else
            {
                _logger.LogInformation("Disabling PIVX payment method for store {StoreId}", store.Id);
                blob.SetExcluded(pmi, true);
            }

            store.SetStoreBlob(blob);
            await _storeRepository.UpdateStore(store);

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = StringLocalizer["PIVX payment method updated successfully"].Value
            });

            return RedirectToAction(nameof(GetStorePivxPaymentMethod), new { storeId = store.Id });
        }

        public class PivxPaymentMethodViewModel
        {
            public string CryptoCode { get; set; } = "PIVX";
            public bool Enabled { get; set; }
            public bool UseShieldedAddresses { get; set; }
            public bool WatchOnly { get; set; }
            public string AccountXpub { get; set; } = "";
            public string SaplingViewingKey { get; set; } = "";
            public int? ViewingKeyBirthHeight { get; set; }
            public string? FirstDerivedAddress { get; set; }
            public bool DaemonAvailable { get; set; }
            public string DaemonInfo { get; set; } = "";
        }
    }
}
