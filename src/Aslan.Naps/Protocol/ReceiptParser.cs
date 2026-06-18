namespace Aslan.Naps.Protocol;

/// <summary>
/// Parses the DP (010) tag value from a NAPS terminal into discrete receipt lines.
///
/// DP contains nested TLV sub-tags per receipt entry, with '*' as the group separator:
///   030 — line number (2 chars)
///   031 — format: S=Simple, G=Gras/Bold
///   032 — alignment: C=Centre, D=Droite(Right), G=Gauche(Left)
///   033 — content (variable length)
///
/// Splitting on '*' before parsing is required because digit sequences inside
/// field values (PANs, amounts) would otherwise be misread as TLV tags.
/// </summary>
public static class ReceiptParser
{
    public static List<ReceiptLine> Parse(string dpValue)
    {
        var lines = new List<ReceiptLine>();

        foreach (var entry in dpValue.Split('*'))
        {
            if (entry.Length < 6) continue;

            string lineNumber = "";
            string format     = "S";
            string alignment  = "G";
            string content    = "";

            var index = 0;
            while (index + 6 <= entry.Length)
            {
                var tag    = entry.Substring(index, 3);
                var lenStr = entry.Substring(index + 3, 3);

                if (!int.TryParse(lenStr, out var length)) break;
                if (index + 6 + length > entry.Length) break;

                var value = entry.Substring(index + 6, length);

                switch (tag)
                {
                    case "030": lineNumber = value; break;
                    case "031": format     = value; break;
                    case "032": alignment  = value; break;
                    case "033": content    = value; break;
                }

                index += 6 + length;
            }

            if (lineNumber.Length == 0) continue;

            lines.Add(new ReceiptLine(
                LineNumber: lineNumber,
                Text:       content,
                Bold:       format == "G",
                Alignment:  alignment switch
                {
                    "C" => ReceiptAlignment.Center,
                    "D" => ReceiptAlignment.Right,
                    _   => ReceiptAlignment.Left
                }
            ));
        }

        return lines;
    }

    /// <summary>
    /// Render receipt lines as plain text for printing or logging.
    /// </summary>
    public static string ToPlainText(IEnumerable<ReceiptLine> lines, int width = 40)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var text = line.Alignment switch
            {
                ReceiptAlignment.Center => line.Text.PadLeft((width + line.Text.Length) / 2).PadRight(width),
                ReceiptAlignment.Right  => line.Text.PadLeft(width),
                _                       => line.Text
            };
            sb.AppendLine(text);
        }
        return sb.ToString();
    }
}

public record ReceiptLine(
    string LineNumber,
    string Text,
    bool Bold,
    ReceiptAlignment Alignment
);

public enum ReceiptAlignment { Left, Center, Right }
