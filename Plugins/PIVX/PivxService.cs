using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.PIVX.Payments;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.PIVX;

/// <summary>
/// Polls pivxd and registers payments for monitored PIVX invoices.
/// Payments are registered through PaymentService so BTCPay's invoice state
/// machine handles partial payments, confirmations and settlement.
/// </summary>
public class PivxService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    private readonly ILogger<PivxService> _log;
    private readonly InvoiceRepository _invoices;
    private readonly PaymentService _paymentService;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly EventAggregator _eventAggregator;
    private readonly PivxRpcClient _rpc;
    private readonly PivxLikeSpecificBtcPayNetwork _network;
    private readonly PivxSettings _cfg;

    public PivxService(ILogger<PivxService> log,
                       InvoiceRepository invoices,
                       PaymentService paymentService,
                       PaymentMethodHandlerDictionary handlers,
                       EventAggregator eventAggregator,
                       PivxRpcClient rpc,
                       PivxLikeSpecificBtcPayNetwork network,
                       IOptions<PivxSettings> cfg)
    {
        _log = log;
        _invoices = invoices;
        _paymentService = paymentService;
        _handlers = handlers;
        _eventAggregator = eventAggregator;
        _rpc = rpc;
        _network = network;
        _cfg = cfg.Value;
    }

    public static long ConfirmationsRequired(SpeedPolicy speedPolicy)
        => speedPolicy switch
        {
            SpeedPolicy.HighSpeed => 0,
            SpeedPolicy.MediumSpeed => 1,
            SpeedPolicy.LowMediumSpeed => 2,
            SpeedPolicy.LowSpeed => 6,
            _ => 6,
        };

    private long RequiredConfirmations(SpeedPolicy speedPolicy)
        => Math.Max(ConfirmationsRequired(speedPolicy), _cfg.MinConfirmations);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_cfg.DaemonUri))
        {
            _log.LogWarning("PIVX daemon not configured. Set BTCPAY_PIVX_DAEMON_URI, BTCPAY_PIVX_RPCUSER, BTCPAY_PIVX_RPCPASSWORD");
        }
        else
        {
            _log.LogInformation("PIVX service started. Daemon at {DaemonUri}, MinConf floor={MinConf}", _cfg.DaemonUri, _cfg.MinConfirmations);
        }

        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(_network.CryptoCode);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_cfg.DaemonUri))
                    await ScanOnce(pmi, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "PIVX scan loop error");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task ScanOnce(PaymentMethodId pmi, CancellationToken ct)
    {
        var invoices = await _invoices.GetMonitoredInvoices(pmi, ct);
        if (invoices.Length == 0)
            return;

        var handler = (PivxPaymentMethodHandler)_handlers[pmi];
        var updatedPayments = new List<(PaymentEntity Payment, InvoiceEntity Invoice)>();

        foreach (var invoice in invoices)
        {
            try
            {
                await ScanInvoice(invoice, pmi, handler, updatedPayments, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "PIVX scan error for invoice {Id}", invoice.Id);
            }
        }

        if (updatedPayments.Count > 0)
        {
            await _paymentService.UpdatePayments(updatedPayments.Select(t => t.Payment).ToList());
            foreach (var group in updatedPayments.GroupBy(t => t.Invoice.Id))
                _eventAggregator.Publish(new InvoiceNeedUpdateEvent(group.Key));
        }
    }

    private async Task ScanInvoice(InvoiceEntity invoice, PaymentMethodId pmi, PivxPaymentMethodHandler handler,
        List<(PaymentEntity Payment, InvoiceEntity Invoice)> updatedPayments, CancellationToken ct)
    {
        var prompt = invoice.GetPaymentPrompt(pmi);
        if (prompt is null || !prompt.Activated || string.IsNullOrEmpty(prompt.Destination))
            return;

        var details = handler.ParsePaymentPromptDetails(prompt.Details);
        var existingPayments = invoice.GetPayments(false)
            .Where(p => p.PaymentMethodId == pmi)
            .Select(p => (Payment: p, Data: (PivxPaymentData)handler.ParsePaymentDetails(p.Details)))
            .ToList();

        var receipts = details.IsShielded
            ? await GetShieldedReceipts(prompt.Destination, ct)
            : await GetTransparentReceipts(prompt.Destination, existingPayments, ct);

        var requiredConfirmations = RequiredConfirmations(invoice.SpeedPolicy);

        foreach (var receipt in receipts)
        {
            var status = receipt.Confirmations >= requiredConfirmations ? PaymentStatus.Settled : PaymentStatus.Processing;
            var existing = existingPayments.FirstOrDefault(p => p.Payment.Id == receipt.PaymentId);

            if (existing.Payment is null)
            {
                var paymentDetails = new PivxPaymentData
                {
                    TransactionId = receipt.TransactionId,
                    OutputIndex = receipt.OutputIndex,
                    Address = prompt.Destination,
                    Amount = receipt.Amount,
                    ConfirmationCount = receipt.Confirmations,
                    IsShielded = details.IsShielded
                };
                var paymentData = new PaymentData
                {
                    Status = status,
                    Amount = receipt.Amount,
                    Created = DateTimeOffset.UtcNow,
                    Id = receipt.PaymentId,
                    Currency = _network.CryptoCode
                }.Set(invoice, handler, paymentDetails);

                var payment = await _paymentService.AddPayment(paymentData, new HashSet<string> { receipt.TransactionId });
                if (payment != null)
                {
                    _log.LogInformation("Invoice {Id}: registered PIVX payment {Amount} ({Confirmations} conf, {Status})",
                        invoice.Id, receipt.Amount, receipt.Confirmations, status);
                    _eventAggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
                }
            }
            else if (existing.Payment.Status != status || existing.Data.ConfirmationCount != receipt.Confirmations)
            {
                existing.Data.ConfirmationCount = receipt.Confirmations;
                existing.Payment.Status = status;
                existing.Payment.Details = JToken.FromObject(existing.Data, handler.Serializer);
                updatedPayments.Add((existing.Payment, invoice));
            }
        }
    }

    private record Receipt(string PaymentId, string TransactionId, int OutputIndex, decimal Amount, long Confirmations);

    private async Task<List<Receipt>> GetTransparentReceipts(string address,
        List<(PaymentEntity Payment, PivxPaymentData Data)> existingPayments, CancellationToken ct)
    {
        var receipts = new List<Receipt>();
        var unspent = await _rpc.ListUnspentAsync(address, 0, ct: ct);
        foreach (var utxo in unspent)
        {
            receipts.Add(new Receipt($"{utxo.txid}-{utxo.vout}", utxo.txid, utxo.vout, utxo.amount, utxo.confirmations));
        }

        // Outputs no longer in listunspent (e.g. already spent by the merchant) still
        // need confirmation updates until they settle, so refresh them via gettransaction.
        foreach (var (_, data) in existingPayments)
        {
            if (data.TransactionId is null || receipts.Any(r => r.TransactionId == data.TransactionId && r.OutputIndex == data.OutputIndex))
                continue;
            var tx = await _rpc.GetTransactionAsync(data.TransactionId, ct);
            receipts.Add(new Receipt($"{data.TransactionId}-{data.OutputIndex}", data.TransactionId, data.OutputIndex, data.Amount, tx.confirmations));
        }

        return receipts;
    }

    private async Task<List<Receipt>> GetShieldedReceipts(string address, CancellationToken ct)
    {
        List<PivxRpcClient.ShieldRecv>? received;
        try
        {
            received = await _rpc.ListReceivedByShieldAddressAsync(address, 0, ct);
        }
        // For watch-only stores the wallet only learns a diversified address
        // once a note for it arrives; until then the daemon rejects the query.
        catch (Exception ex) when (ex.Message.Contains("does not belong"))
        {
            return new List<Receipt>();
        }

        return (received ?? new List<PivxRpcClient.ShieldRecv>())
            .Where(r => r.change is not true)
            .Select(r => new Receipt($"{r.txid}-{r.outindex ?? 0}", r.txid, r.outindex ?? 0, r.amount, r.confirmations))
            .ToList();
    }
}
