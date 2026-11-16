using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Hesabyar.Infrastructure.Persistence;

namespace Hesabyar.App.Services;

public sealed class ReportsExportService
{
    public async Task<string> CreateCsvAsync(FinancialReportSnapshot report, CancellationToken cancellationToken = default)
    {
        var rows = BuildCsvRows(report);
        var builder = new StringBuilder();
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        var path = Path.Combine(FileSystem.CacheDirectory, $"hesabyar-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(true), cancellationToken);
        return path;
    }

    public async Task<string> CreateExcelAsync(FinancialReportSnapshot report, CancellationToken cancellationToken = default)
    {
        var sheets = BuildSheets(report);
        var path = Path.Combine(FileSystem.CacheDirectory, $"hesabyar-report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WriteEntry(archive, "[Content_Types].xml", BuildContentTypes(sheets.Count));
            WriteEntry(archive, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            WriteEntry(archive, "xl/workbook.xml", BuildWorkbook(sheets));
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(sheets.Count));
            WriteEntry(archive, "xl/styles.xml", StylesXml);
            for (var index = 0; index < sheets.Count; index++)
                WriteEntry(archive, $"xl/worksheets/sheet{index + 1}.xml", BuildWorksheet(sheets[index].Rows));
        }
        await stream.FlushAsync(cancellationToken);
        return path;
    }

    private static IReadOnlyList<ReportSheet> BuildSheets(FinancialReportSnapshot report)
    {
        return
        [
            new("Summary",
            [
                ["Metric", "Amount (IRR)"],
                ["From date", report.FromDate.ToString("yyyy-MM-dd")],
                ["To date", report.ToDate.ToString("yyyy-MM-dd")],
                ["Income", report.IncomeMinor],
                ["Expenses", report.ExpenseMinor],
                ["Net cash flow", checked(report.IncomeMinor - report.ExpenseMinor)],
                ["Assets", report.AssetsMinor],
                ["Liabilities", report.LiabilitiesMinor],
                ["Net worth", report.NetWorthMinor]
            ]),
            new("Monthly",
                new[] { new object?[] { "Persian month", "Income (IRR)", "Expenses (IRR)", "Net (IRR)" } }
                .Concat(report.MonthlyTrend.Select(x => (object?[])[x.Label, x.IncomeMinor, x.ExpenseMinor, checked(x.IncomeMinor - x.ExpenseMinor)]))
                .ToArray()),
            new("Categories",
                new[] { new object?[] { "Expense category", "Amount (IRR)" } }
                .Concat(report.ExpensesByCategory.Select(x => (object?[])[x.Name, x.AmountMinor])).ToArray()),
            new("Accounts",
                new[] { new object?[] { "Account", "Type", "Balance (IRR)" } }
                .Concat(report.Accounts.Select(x => (object?[])[x.Name, x.Type, x.BalanceMinor])).ToArray()),
            new("People",
                new[] { new object?[] { "Person", "Balance (IRR)" } }
                .Concat(report.People.Select(x => (object?[])[x.Name, x.BalanceMinor])).ToArray())
        ];
    }

    private static IReadOnlyList<string[]> BuildCsvRows(FinancialReportSnapshot report)
    {
        var rows = new List<string[]>
        {
            ["Summary", "Amount (IRR)"],
            ["From date", report.FromDate.ToString("yyyy-MM-dd")],
            ["To date", report.ToDate.ToString("yyyy-MM-dd")],
            ["Income", report.IncomeMinor.ToString()],
            ["Expenses", report.ExpenseMinor.ToString()],
            ["Net cash flow", checked(report.IncomeMinor - report.ExpenseMinor).ToString()],
            ["Assets", report.AssetsMinor.ToString()],
            ["Liabilities", report.LiabilitiesMinor.ToString()],
            ["Net worth", report.NetWorthMinor.ToString()],
            [],
            ["Monthly trend", "Income (IRR)", "Expenses (IRR)", "Net (IRR)"]
        };
        rows.AddRange(report.MonthlyTrend.Select(x => new[]
        {
            x.Label, x.IncomeMinor.ToString(), x.ExpenseMinor.ToString(),
            checked(x.IncomeMinor - x.ExpenseMinor).ToString()
        }));
        rows.Add([]);
        rows.Add(["Expense categories", "Amount (IRR)"]);
        rows.AddRange(report.ExpensesByCategory.Select(x => new[] { x.Name, x.AmountMinor.ToString() }));
        rows.Add([]);
        rows.Add(["Accounts", "Type", "Balance (IRR)"]);
        rows.AddRange(report.Accounts.Select(x => new[] { x.Name, x.Type, x.BalanceMinor.ToString() }));
        rows.Add([]);
        rows.Add(["People", "Balance (IRR)"]);
        rows.AddRange(report.People.Select(x => new[] { x.Name, x.BalanceMinor.ToString() }));
        return rows;
    }

    private static string EscapeCsv(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        return value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string BuildContentTypes(int count)
    {
        var sheets = string.Concat(Enumerable.Range(1, count).Select(i =>
            $"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" + sheets + "</Types>";
    }

    private static string BuildWorkbook(IReadOnlyList<ReportSheet> sheets)
    {
        var names = string.Concat(sheets.Select((x, i) =>
            $"<sheet name=\"{Xml(x.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets>" + names + "</sheets></workbook>";
    }

    private static string BuildWorkbookRelationships(int count)
    {
        var sheets = string.Concat(Enumerable.Range(1, count).Select(i =>
            $"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + sheets +
            $"<Relationship Id=\"rId{count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
    }

    private static string BuildWorksheet(IReadOnlyList<object?[]> rows)
    {
        var builder = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\" rightToLeft=\"1\"/></sheetViews><sheetFormatPr defaultRowHeight=\"18\"/><sheetData>");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            builder.Append($"<row r=\"{rowIndex + 1}\">");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Length; columnIndex++)
            {
                var value = rows[rowIndex][columnIndex];
                if (value is null) continue;
                var reference = $"{ColumnName(columnIndex + 1)}{rowIndex + 1}";
                var style = rowIndex == 0 ? " s=\"1\"" : value is long or int ? " s=\"2\"" : string.Empty;
                if (value is long or int)
                    builder.Append($"<c r=\"{reference}\"{style}><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else
                    builder.Append($"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{Xml(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}</t></is></c>");
            }
            builder.Append("</row>");
        }
        builder.Append("</sheetData></worksheet>");
        return builder.ToString();
    }

    private static string ColumnName(int index)
    {
        var result = string.Empty;
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index /= 26;
        }
        return result;
    }

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private const string StylesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"#,##0;[Red]-#,##0\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Tahoma\"/></font><font><b/><sz val=\"11\"/><name val=\"Tahoma\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>";

    private sealed record ReportSheet(string Name, IReadOnlyList<object?[]> Rows);
}
