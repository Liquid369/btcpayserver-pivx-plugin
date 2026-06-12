using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PIVX.Payments;
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
                DaemonAvailable = daemonAvailable,
                DaemonInfo = blockchainInfo
            };

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
                // Enable PIVX payment method
                _logger.LogInformation("Enabling PIVX payment method for store {StoreId}", store.Id);
                
                store.SetPaymentMethodConfig(_handlers[pmi], new PivxPaymentMethodConfig
                {
                    UseShieldedAddresses = viewModel.UseShieldedAddresses
                });

                // Remove from excluded payment methods
                blob.SetExcluded(pmi, false);
            }
            else
            {
                // Disable PIVX payment method
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
            public bool DaemonAvailable { get; set; }
            public string DaemonInfo { get; set; } = "";
        }
    }
}
