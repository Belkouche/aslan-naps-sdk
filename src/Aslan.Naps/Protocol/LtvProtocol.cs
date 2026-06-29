namespace Aslan.Naps.Protocol;

/// <summary>
/// NAPS LTV (Tag-Length-Value) ASCII protocol for Ingenico Lane/3000.
/// Wire format: TAG(3 ASCII digits) + LENGTH(3 ASCII digits, zero-padded) + VALUE(LENGTH chars)
/// Messages terminated by '!' character.
/// </summary>
public static class LtvProtocol
{
    public static readonly HashSet<string> ApprovedCodes = new() { "000", "001", "003", "007" };

    // Tags
    public const string TagTm = "001";    // Message type
    public const string TagMt = "002";    // Amount (centimes, 12 digits)
    public const string TagNcai = "003";  // Register(2) + Cashier(5)
    public const string TagNs = "004";    // Sequence number (6 digits)
    public const string TagNcar = "007";  // Card number (masked PAN)
    public const string TagStan = "008";  // STAN
    public const string TagNa = "009";    // Authorization number
    public const string TagDp = "010";    // Receipt/print data
    public const string TagDv = "012";    // Currency code
    public const string TagCr = "013";    // Response code
    public const string TagDa = "014";    // Date DDMMYYYY
    public const string TagHe = "015";    // Time HHMMSS
    public const string TagNprt = "016";  // Cardholder name
    public const string TagDaex = "017";  // Card expiry YYMM
    public const string TagDatr = "018";  // Transaction date DDMMYYYY (required by terminal)
    public const string TagHetr = "019";  // Transaction time HHMMSS (required by terminal)

    // Message types
    public const string TmPayment = "001";
    public const string TmConfirmation = "002";
    public const string TmCancellation = "003";
    public const string TmDuplicate = "008";
    public const string TmNetworkTest = "009";
    public const string TmTotals = "010";
    public const string TmResetPinpad = "012";
    public const string TmReferencing = "013";

    public static string Tlv(string tag, string value)
    {
        return $"{tag}{value.Length.ToString().PadLeft(3, '0')}{value}";
    }

    public static string BuildMessage(string tm, string ncai = "0100001", string ns = "000001",
        long? amountCentimes = null, string currency = "504",
        IEnumerable<(string tag, string value)>? extraFields = null)
    {
        var now = DateTime.Now;
        var fields = new List<string> { Tlv(TagTm, tm) };

        if (amountCentimes.HasValue)
            fields.Add(Tlv(TagMt, amountCentimes.Value.ToString().PadLeft(12, '0')));

        fields.Add(Tlv(TagNcai, ncai));
        fields.Add(Tlv(TagNs, ns));

        if (amountCentimes.HasValue)
            fields.Add(Tlv(TagDv, currency));

        fields.Add(Tlv(TagDa, now.ToString("ddMMyyyy")));
        fields.Add(Tlv(TagHe, now.ToString("HHmmss")));
        fields.Add(Tlv(TagDatr, now.ToString("ddMMyyyy")));
        fields.Add(Tlv(TagHetr, now.ToString("HHmmss")));

        if (extraFields != null)
            foreach (var (tag, value) in extraFields)
                fields.Add(Tlv(tag, value));

        return string.Concat(fields) + "!";
    }

    public static string BuildConfirmation(string stan, string ncai = "0100001", string ns = "000001")
    {
        var now = DateTime.Now;
        var fields = new[]
        {
            Tlv(TagTm, TmConfirmation),
            Tlv(TagNcai, ncai),
            Tlv(TagNs, ns),
            Tlv(TagStan, stan),
            Tlv(TagDa, now.ToString("ddMMyyyy")),
            Tlv(TagHe, now.ToString("HHmmss"))
        };
        return string.Concat(fields) + "!";
    }

    public static Dictionary<string, string> ParseMessage(string raw)
    {
        var data = raw.Trim().TrimEnd('!');
        var fields = new Dictionary<string, string>();
        var pos = 0;

        while (pos + 6 <= data.Length)
        {
            var tag = data.Substring(pos, 3);
            var lengthStr = data.Substring(pos + 3, 3);

            if (!int.TryParse(lengthStr, out var length) || !tag.All(char.IsDigit))
                break;
            if (pos + 6 + length > data.Length)
                break;

            fields[tag] = data.Substring(pos + 6, length);
            pos += 6 + length;
        }

        return fields;
    }

    public static bool IsApproved(string? cr) => cr != null && ApprovedCodes.Contains(cr);

    public static string MaskPan(string pan)
    {
        if (pan.Length < 10) return pan;
        return pan.Substring(0, 6) + new string('*', pan.Length - 10) + pan.Substring(pan.Length - 4);
    }

    public static string ExtractLastMessage(string raw)
    {
        var messages = raw.Split(new[] { '!' }, StringSplitOptions.RemoveEmptyEntries);
        return messages.Length == 0 ? "" : messages[messages.Length - 1] + "!";
    }
}
