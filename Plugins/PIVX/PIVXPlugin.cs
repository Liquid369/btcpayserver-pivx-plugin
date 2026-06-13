using System.Globalization;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PIVX.Payments;
using BTCPayServer.Plugins.PIVX.Services;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.PIVX;

public class PivxPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies =>
        new[] { new IBTCPayServerPlugin.PluginDependency { Identifier = "BTCPayServer", Condition = ">=2.1.0" } };

    public override void Execute(IServiceCollection services)
    {
        var network = PivxLikeSpecificBtcPayNetwork.Instance;
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);

        // Network registration
        services.AddBTCPayNetwork(network)
                .AddTransactionLinkProvider(pmi, new SimpleTransactionLinkProvider("https://explorer.duddino.com/tx/{0}"));
        services.AddDefaultPrettyName(pmi, network.DisplayName);
        services.AddSingleton(network);

        // Daemon configuration (BTCPAY_PIVX_* environment variables)
        services.AddOptions<PivxSettings>()
                .BindConfiguration("PIVX")
                .PostConfigure(s => s.NormalizeFromEnv());

        // RPC client to pivxd
        services.AddHttpClient<PivxRpcClient>()
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        // Watch-only support: xpub derivation + pivx-walletd for shield addresses
        services.AddHttpClient<PivxWalletdClient>()
                .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<PivxWatchOnlyService>();

        // Payment method handler
        services.AddSingleton<IPaymentMethodHandler, PivxPaymentMethodHandler>();

        // Checkout UI + payment link
        services.AddSingleton<IPaymentLinkExtension>(provider =>
            (IPaymentLinkExtension)ActivatorUtilities.CreateInstance(provider, typeof(PivxPaymentLinkExtension), new object[] { pmi, network }));
        services.AddSingleton<ICheckoutModelExtension>(provider =>
            (ICheckoutModelExtension)ActivatorUtilities.CreateInstance(provider, typeof(PivxCheckoutModelExtension), new object[] { pmi, network }));

        // Background payment detection
        services.AddHostedService<PivxService>();

        // Sync status (dashboard / server info)
        services.AddSingleton<PivxSyncSummaryProvider>();
        services.AddSingleton<ISyncSummaryProvider>(provider => provider.GetRequiredService<PivxSyncSummaryProvider>());

        // Wallet balance for the store dashboard
        services.AddSingleton<PivxBalanceProvider>();

        // UI extensions
        services.AddUIExtension("store-integrations-nav", "PIVX/PivxNav");
        services.AddUIExtension("store-invoices-payments", "PIVX/ViewPivxPaymentData");
        services.AddUIExtension("dashboard", "PIVX/PivxDashboardBalance");
    }

    class SimpleTransactionLinkProvider : DefaultTransactionLinkProvider
    {
        public SimpleTransactionLinkProvider(string blockExplorerLink) : base(blockExplorerLink)
        {
        }

        public override string? GetTransactionLink(string paymentId)
        {
            if (string.IsNullOrEmpty(BlockExplorerLink))
                return null;
            return string.Format(CultureInfo.InvariantCulture, BlockExplorerLink, paymentId);
        }
    }
}
