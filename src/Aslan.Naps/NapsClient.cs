using Aslan.Naps.Models;
using Aslan.Naps.Polyfills;
using Aslan.Naps.Protocol;
using Aslan.Naps.Transport;

namespace Aslan.Naps;

/// <summary>
/// Unified NAPS payment terminal client.
/// Supports Sunmi P2/P3 (TCP socket) and Ingenico Lane/3000 (USB serial)
/// through a single API with a transport switch.
/// </summary>
public class NapsClient : IDisposable
{
    private readonly NapsClientOptions _options;
    private readonly ITerminalTransport _transport;
    private int _sequence = 1;
    private readonly object _seqLock = new();

    public NapsClient(NapsClientOptions options)
    {
        _options = options;
        _transport = options.Transport switch
        {
            TransportType.TcpSocket => new TcpSocketTransport(options),
            TransportType.UsbSerial => new SerialTransport(options),
            _ => throw new ArgumentException($"Unknown transport: {options.Transport}")
        };
    }

    public NapsClient(TransportType transport) : this(new NapsClientOptions { Transport = transport }) { }

    public bool IsConnected => _transport.IsConnected;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _transport.ConnectAsync(ct);
    }

    public void Disconnect() => _transport.Disconnect();

    /// <summary>
    /// Process a payment. Two-phase: authorization (TM=001) then confirmation (TM=002).
    /// </summary>
    public async Task<PaymentResult> PayAsync(decimal amountMad, string? orderId = null, CancellationToken ct = default)
    {
        var amountCentimes = (long)Math.Round(amountMad * 100);
        var ncai = _options.RegisterId + _options.CashierId;
        var ns = NextSequence();

        // Phase 1: Authorization
        var payMsg = LtvProtocol.BuildMessage(LtvProtocol.TmPayment, ncai, ns, amountCentimes);
        string payResp;
        try
        {
            payResp = await _transport.SendReceiveAsync(payMsg, _options.PaymentTimeoutMs, ct);
        }
        catch (TimeoutException)
        {
            return new PaymentResult { ResponseCode = "TIMEOUT", ResponseMessage = "Payment request timed out" };
        }

        var payFields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(payResp));
        var cr = payFields.GetValueOrDefault(LtvProtocol.TagCr);

        if (!LtvProtocol.IsApproved(cr))
            return MapToResult(payFields, false);

        // Phase 2: Confirmation (within 40 seconds)
        var stan = payFields.GetValueOrDefault(LtvProtocol.TagStan);
        if (stan == null)
            return MapToResult(payFields, true); // Approved but no STAN -- cannot confirm

        var confMsg = LtvProtocol.BuildConfirmation(stan, ncai, ns);
        try
        {
            var confResp = await _transport.SendReceiveAsync(confMsg, _options.ConfirmationTimeoutMs, ct);
            var confFields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(confResp));
            var confCr = confFields.GetValueOrDefault(LtvProtocol.TagCr);

            var result = MapToResult(payFields, true);
            if (confCr != "000")
                return result with { ResponseMessage = $"Confirmation failed: CR={confCr}" };
            return result;
        }
        catch (TimeoutException)
        {
            return MapToResult(payFields, true) with { ResponseMessage = "Confirmation timed out" };
        }
    }

    public async Task<PaymentResult> CancelAsync(string stan, CancellationToken ct = default)
    {
        var ncai = _options.RegisterId + _options.CashierId;
        var ns = NextSequence();
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmCancellation, ncai, ns,
            extraFields: new[] { (LtvProtocol.TagStan, stan) });

        try
        {
            var resp = await _transport.SendReceiveAsync(msg, _options.TestTimeoutMs, ct);
            var fields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(resp));
            return MapToResult(fields, false);
        }
        catch (TimeoutException)
        {
            return new PaymentResult { ResponseCode = "TIMEOUT", ResponseMessage = "Cancel request timed out" };
        }
    }

    public async Task<TestResult> NetworkTestAsync(CancellationToken ct = default)
    {
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmNetworkTest);
        var resp = await _transport.SendReceiveAsync(msg, _options.TestTimeoutMs, ct);
        var fields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(resp));
        var cr = fields.GetValueOrDefault(LtvProtocol.TagCr);
        return new TestResult
        {
            IsSuccess = cr == "000",
            ResponseCode = cr,
            Message = cr == "000" ? "Network OK" : $"Network test failed: CR={cr}"
        };
    }

    public async Task<ReferencingResult> ReferencingAsync(CancellationToken ct = default)
    {
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmReferencing);
        var resp = await _transport.SendReceiveAsync(msg, _options.ReferencingTimeoutMs, ct);
        var fields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(resp));
        var cr = fields.GetValueOrDefault(LtvProtocol.TagCr);
        return new ReferencingResult
        {
            IsSuccess = cr == "000",
            ResponseCode = cr,
            ReceiptText = fields.GetValueOrDefault(LtvProtocol.TagDp)
        };
    }

    public async Task<TotalsResult> TotalsAsync(CancellationToken ct = default)
    {
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmTotals);
        var resp = await _transport.SendReceiveAsync(msg, _options.TestTimeoutMs, ct);
        var fields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(resp));
        var cr = fields.GetValueOrDefault(LtvProtocol.TagCr);
        return new TotalsResult
        {
            IsSuccess = cr == "000",
            ResponseCode = cr,
            ReceiptText = fields.GetValueOrDefault(LtvProtocol.TagDp)
        };
    }

    public async Task<PaymentResult> DuplicateReceiptAsync(CancellationToken ct = default)
    {
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmDuplicate);
        var resp = await _transport.SendReceiveAsync(msg, 15_000, ct);
        var fields = LtvProtocol.ParseMessage(LtvProtocol.ExtractLastMessage(resp));
        return MapToResult(fields, false);
    }

    public async Task ResetPinPadAsync(CancellationToken ct = default)
    {
        var msg = LtvProtocol.BuildMessage(LtvProtocol.TmResetPinpad);
        await _transport.SendReceiveAsync(msg, 15_000, ct);
    }

    /// <summary>Auto-discover the terminal port (USB serial only).</summary>
    public static string? FindPort() => PortDiscovery.FindIngenicoPort();

    /// <summary>List all available serial ports.</summary>
    public static string[] ListPorts() => PortDiscovery.ListPorts();

    private string NextSequence()
    {
        lock (_seqLock)
        {
            var ns = _sequence.ToString().PadLeft(6, '0');
            _sequence = (_sequence % 999999) + 1;
            return ns;
        }
    }

    private static PaymentResult MapToResult(Dictionary<string, string> fields, bool isApproved)
    {
        var cr = fields.GetValueOrDefault(LtvProtocol.TagCr);
        var cardNumber = fields.GetValueOrDefault(LtvProtocol.TagNcar);
        var dp = fields.GetValueOrDefault(LtvProtocol.TagDp);

        return new PaymentResult
        {
            IsSuccess = isApproved && LtvProtocol.IsApproved(cr),
            IsCancelled = cr is "280" or "480" or "099",
            ResponseCode = cr,
            ResponseMessage = dp,
            AuthorizationNumber = fields.GetValueOrDefault(LtvProtocol.TagNa),
            CardNumber = cardNumber != null ? LtvProtocol.MaskPan(cardNumber) : null,
            Stan = fields.GetValueOrDefault(LtvProtocol.TagStan),
            ApprovalCode = fields.GetValueOrDefault(LtvProtocol.TagNa),
            TerminalId = fields.GetValueOrDefault("020"),
            TransactionDate = fields.GetValueOrDefault(LtvProtocol.TagDa),
            TransactionTime = fields.GetValueOrDefault(LtvProtocol.TagHe),
            ReceiptText = dp,
            ReceiptLines = dp != null ? ReceiptParser.Parse(dp) : null,
            CardholderName = fields.GetValueOrDefault(LtvProtocol.TagNprt),
            CardScheme = fields.GetValueOrDefault("018"),
            Rrn = null,
        };
    }

    public void Dispose() => _transport.Dispose();
}
