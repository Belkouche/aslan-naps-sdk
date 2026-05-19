namespace Aslan.Naps.Models;

public record PaymentResult
{
    public bool IsSuccess { get; init; }
    public bool IsCancelled { get; init; }
    public string? ResponseCode { get; init; }
    public string? ResponseMessage { get; init; }
    public string? AuthorizationNumber { get; init; }
    public string? CardNumber { get; init; }
    public string? CardScheme { get; init; }
    public string? Stan { get; init; }
    public string? Rrn { get; init; }
    public string? ApprovalCode { get; init; }
    public string? TerminalId { get; init; }
    public string? TransactionDate { get; init; }
    public string? TransactionTime { get; init; }
    public string? ReceiptText { get; init; }
    public string? CardholderName { get; init; }
    public string? MerchantName { get; init; }
    public string? MerchantCity { get; init; }

    public bool ShouldRetry => ResponseCode is "909" or "911" or "T01";
}

public class TestResult
{
    public bool IsSuccess { get; init; }
    public string? ResponseCode { get; init; }
    public string? Message { get; init; }
}

public class ReferencingResult
{
    public bool IsSuccess { get; init; }
    public string? ResponseCode { get; init; }
    public string? ReceiptText { get; init; }
}

public class TotalsResult
{
    public bool IsSuccess { get; init; }
    public string? ResponseCode { get; init; }
    public string? ReceiptText { get; init; }
}
