using System.Text;
using MiniExcelLibs;

namespace WaPlatform.Api.Services;

public record SheetData(List<string> Columns, List<List<string>> Rows);

public static class Csv
{
    /// <summary>CSV with a UTF-8 BOM so Excel shows Arabic names correctly.</summary>
    public static byte[] Write(IEnumerable<string> header, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", header.Select(Escape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(v => Escape(v switch
            {
                null => "",
                DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss") + "Z",
                bool b => b ? "yes" : "no",
                _ => v.ToString() ?? "",
            }))));
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Escape(string v)
    {
        // A leading =, +, - or @ would make Excel treat the cell as a formula.
        if (v.Length > 0 && "=+-@".Contains(v[0]) && !double.TryParse(v, out _)) v = "'" + v;
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>Reads the first sheet of an .xlsx or a .csv file; the first row is the header.</summary>
    public static SheetData Read(Stream stream, string fileName, int maxRows)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var type = ext switch
        {
            ".xlsx" => ExcelType.XLSX,
            ".csv" => ExcelType.CSV,
            _ => throw new InvalidDataException("Upload an Excel (.xlsx) or CSV file."),
        };

        // Seekable copy: MiniExcel needs it for xlsx, and it lets us skip a UTF-8 BOM in CSVs.
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        var rows = MiniExcel.Query(ms, useHeaderRow: false, excelType: type)
            .Cast<IDictionary<string, object?>>()
            .Select(r => r.Values.Select(v => Cell(v)).ToList())
            .Where(r => r.Any(c => c.Length > 0))
            .Take(maxRows + 2)
            .ToList();
        if (rows.Count == 0) throw new InvalidDataException("The file is empty.");

        var header = rows[0].Select((c, i) => c.Length > 0 ? c.TrimStart('﻿') : $"Column {i + 1}").ToList();
        var data = rows.Skip(1).Select(r => r.Take(header.Count).Concat(Enumerable.Repeat("", Math.Max(0, header.Count - r.Count))).ToList()).ToList();
        if (data.Count > maxRows) throw new InvalidDataException($"The file has more than {maxRows} rows. Split it into smaller files.");
        return new SheetData(header, data);
    }

    private static string Cell(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
        // Excel stores phone numbers as numbers: 962791234567 must not become 9.62791E+11.
        double d => d % 1 == 0 && Math.Abs(d) < 1e15 ? ((long)d).ToString() : d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString()?.Trim() ?? "",
    };
}
