using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Plugins.PIVX.Payments
{
    public class PivxCheckoutModelExtension : ICheckoutModelExtension
    {
        private readonly PivxLikeSpecificBtcPayNetwork _network;
        private readonly IPaymentLinkExtension _paymentLinkExtension;

        public PivxCheckoutModelExtension(
            PaymentMethodId paymentMethodId,
            PivxLikeSpecificBtcPayNetwork network,
            IEnumerable<IPaymentLinkExtension> paymentLinkExtensions)
        {
            PaymentMethodId = paymentMethodId;
            _network = network;
            _paymentLinkExtension = paymentLinkExtensions.Single(p => p.PaymentMethodId == PaymentMethodId);
        }

        public PaymentMethodId PaymentMethodId { get; }

        public string Image => _network.CryptoImagePath;
        public string Badge => "";

        public void ModifyCheckoutModel(CheckoutModelContext context)
        {
            if (context is not { Handler: PivxPaymentMethodHandler handler })
                return;

            context.Model.CheckoutBodyComponentName = BitcoinCheckoutModelExtension.CheckoutBodyComponentName;
            context.Model.ShowRecommendedFee = false;

            var details = context.InvoiceEntity.GetPayments(true)
                .Where(p => p.PaymentMethodId == PaymentMethodId)
                .Select(p => p.GetDetails<PivxPaymentData>(handler))
                .Where(p => p is not null)
                .FirstOrDefault();
            if (details is not null)
            {
                context.Model.ReceivedConfirmations = (int)details.ConfirmationCount;
                context.Model.RequiredConfirmations = (int)PivxService.ConfirmationsRequired(context.InvoiceEntity.SpeedPolicy);
            }

            context.Model.InvoiceBitcoinUrl = _paymentLinkExtension.GetPaymentLink(context.Prompt, context.UrlHelper);
            context.Model.InvoiceBitcoinUrlQR = context.Model.InvoiceBitcoinUrl;
        }
    }
}
