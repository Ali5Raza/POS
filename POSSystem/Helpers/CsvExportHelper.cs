using System.Text;

namespace POSSystem.Helpers;

public static class CsvExportHelper
{
    public static byte[] BuildCsv<T>(IEnumerable<T> rows, string[] headers, Func<T, IEnumerable<object?>> selector)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            var values = selector(row).Select(v => EscapeCsv(v?.ToString() ?? string.Empty));
            sb.AppendLine(string.Join(",", values));
        }

        // Include UTF-8 BOM so Excel opens UTF-8 CSVs cleanly
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var result = new byte[preamble.Length + body.Length];
        Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
        Buffer.BlockCopy(body, 0, result, preamble.Length, body.Length);
        return result;
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}
