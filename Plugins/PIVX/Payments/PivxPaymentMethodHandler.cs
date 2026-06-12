using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PIVX.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.PIVX.Payments;

/// <summary>
/// PIVX payment handler using Bitcoin-style RPC against pivxd.
/// </summary>
public class PivxPaymentMethodHandler : IPaymentMethodHandler
{
    private readonly PivxRpcClient _rpc;
    private readonly PivxLikeSpecificBtcPayNetwork _network;
    private readonly PivxWatchOnlyService _watchOnly;
    private readonly PivxWalletdClient _walletd;
    private readonly ILogger<PivxPaymentMethodHandler> _logger;

    public PaymentMethodId PaymentMethodId { get; }
    public Newtonsoft.Json.JsonSerializer Serializer { get; }

    public PivxPaymentMethodHandler(
        PivxRpcClient rpc,
        PivxLikeSpecificBtcPayNetwork network,
        PivxWatchOnlyService watchOnly,
        PivxWalletdClient walletd,
        ILogger<PivxPaymentMethodHandler> logger)
    {
        _rpc = rpc;
        _network = network;
        _watchOnly = watchOnly;
        _walletd = walletd;
        _logger = logger;
        PaymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId(_network.CryptoCode);
        Serializer = BlobSerializer.CreateSerializer((NBitcoin.Network?)null).Serializer;
    }

    public Task BeforeFetchingRates(PaymentMethodContext context)
    {
        context.Prompt.Currency = _network.CryptoCode;
        context.Prompt.Divisibility = _network.Divisibility;
        return Task.CompletedTask;
    }

    public async Task ConfigurePrompt(PaymentMethodContext context)
    {
        var invoice = context.InvoiceEntity;
        var config = ParsePaymentMethodConfig(context.PaymentMethodConfig);

        try
        {
            string paymentAddress;
            if (config.AddressSource == PivxAddressSource.WatchOnly)
            {
                paymentAddress = config.UseShieldedAddresses
                    ? await _watchOnly.ReserveShieldAddressAsync(invoice.StoreId, config.SaplingViewingKey
                        ?? throw new InvalidOperationException("Watch-only shielded store has no viewing key configured"))
                    : await _watchOnly.ReserveTransparentAddressAsync(invoice.StoreId, config.AccountXpub
                        ?? throw new InvalidOperationException("Watch-only store has no xpub configured"));
            }
            else
            {
                paymentAddress = config.UseShieldedAddresses
                    ? await _rpc.GetNewShieldAddressAsync()
                    : await _rpc.GetNewAddressAsync($"invoice-{invoice.Id}");
            }

            _logger.LogInformation("Generated PIVX address for invoice {InvoiceId}: {Address}", invoice.Id, paymentAddress);

            context.Prompt.Destination = paymentAddress;
            context.Prompt.PaymentMethodFee = 0;
            context.Prompt.Details = JObject.FromObject(new PivxPaymentPromptDetails
            {
                DepositAddress = paymentAddress,
                IsShielded = config.UseShieldedAddresses
            }, Serializer);

            context.TrackedDestinations.Add(paymentAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate PIVX payment address for invoice {InvoiceId}", invoice.Id);
            throw new PaymentMethodUnavailableException($"Failed to generate PIVX payment address: {ex.Message}");
        }
    }

    public PivxPaymentMethodConfig ParsePaymentMethodConfig(JToken config)
    {
        return config?.ToObject<PivxPaymentMethodConfig>(Serializer)
            ?? new PivxPaymentMethodConfig();
    }

    object IPaymentMethodHandler.ParsePaymentMethodConfig(JToken config)
    {
        return ParsePaymentMethodConfig(config);
    }

    public PivxPaymentPromptDetails ParsePaymentPromptDetails(JToken details)
    {
        return details?.ToObject<PivxPaymentPromptDetails>(Serializer)
            ?? throw new FormatException($"Invalid {nameof(PivxPaymentPromptDetails)}");
    }

    object IPaymentMethodHandler.ParsePaymentPromptDetails(JToken details)
    {
        return ParsePaymentPromptDetails(details);
    }

    public object ParsePaymentDetails(JToken details)
    {
        return details?.ToObject<PivxPaymentData>(Serializer)
            ?? throw new FormatException($"Invalid {nameof(PivxPaymentData)}");
    }

    public async Task ValidatePaymentMethodConfig(PaymentMethodConfigValidationContext validationContext)
    {
        var config = validationContext.Config?.ToObject<PivxPaymentMethodConfig>(Serializer)
            ?? new PivxPaymentMethodConfig();

        try
        {
            var info = await _rpc.GetBlockchainInfoAsync();
            if (info == null)
            {
                validationContext.ModelState.AddModelError(nameof(config), "Unable to connect to PIVX daemon. Please check configuration.");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PIVX daemon connection failed during validation");
            validationContext.ModelState.AddModelError(nameof(config), $"PIVX daemon connection failed: {ex.Message}");
            return;
        }

        if (config.AddressSource == PivxAddressSource.WatchOnly)
        {
            if (config.UseShieldedAddresses)
            {
                if (string.IsNullOrWhiteSpace(config.SaplingViewingKey) || !config.SaplingViewingKey.StartsWith("pxview"))
                {
                    validationContext.ModelState.AddModelError(nameof(config.SaplingViewingKey),
                        "Watch-only shielded payments need a Sapling extended full viewing key (pxviews1...)");
                    return;
                }
                if (!await _walletd.HealthyAsync())
                {
                    validationContext.ModelState.AddModelError(nameof(config.SaplingViewingKey),
                        "pivx-walletd is unreachable. Set BTCPAY_PIVX_WALLETD_URI and make sure the service is running.");
                    return;
                }
            }
            else
            {
                try
                {
                    PivxWatchOnlyService.DeriveTransparentAddress(config.AccountXpub ?? "", 0);
                }
                catch (Exception ex)
                {
                    validationContext.ModelState.AddModelError(nameof(config.AccountXpub),
                        $"Invalid account xpub: {ex.Message}");
                    return;
                }
            }
        }

        validationContext.Config = JToken.FromObject(config, Serializer);
    }
}
