using System.Globalization;
using ClosedXML.Excel;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// The spreadsheet an admin fills in, and the reader that takes it back.
///
/// Both live here on purpose. A template written in one place and parsed in
/// another drifts the first time a column is renamed, and the drift shows up as
/// a file that downloads cleanly and imports as nothing.
/// </summary>
internal static class HolidayWorkbook
{
    /// <summary>What the "Repeats annually" column accepts, either way round.</summary>
    private static readonly string[] Yes = ["yes", "y", "true", "1"];
    private static readonly string[] No = ["no", "n", "false", "0"];

    public static byte[] Write()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(HolidayTemplate.SheetName);

        for (var column = 0; column < HolidayTemplate.Columns.Count; column++)
        {
            var cell = sheet.Cell(1, column + 1);
            cell.Value = HolidayTemplate.Columns[column];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#071955");
            cell.Style.Font.FontColor = XLColor.White;
        }

        // Two rows filled in, one of each kind: the fastest way to say what
        // goes in a column is to show it filled in correctly.
        Example(sheet, 2, "Christmas Day", new DateOnly(DateTime.UtcNow.Year, 12, 25), HolidayKind.Regular, true);
        Example(sheet, 3, "Maundy Thursday", new DateOnly(DateTime.UtcNow.Year, 4, 2), HolidayKind.SpecialNonWorking, false);

        // Dropdowns rather than free text, so the two columns with a fixed set
        // of answers cannot come back misspelled.
        var kinds = sheet.Range(sheet.Cell(2, 3), sheet.Cell(HolidayTemplate.MostRows + 1, 3));
        kinds.CreateDataValidation().List($"\"{string.Join(",", HolidayKind.All)}\"", true);

        var repeats = sheet.Range(sheet.Cell(2, 4), sheet.Cell(HolidayTemplate.MostRows + 1, 4));
        repeats.CreateDataValidation().List("\"Yes,No\"", true);

        sheet.Column(2).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns(1, HolidayTemplate.Columns.Count).AdjustToContents();
        sheet.SheetView.FreezeRows(1);

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);

        return buffer.ToArray();
    }

    private static void Example(IXLWorksheet sheet, int row, string name, DateOnly date, string kind, bool repeats)
    {
        sheet.Cell(row, 1).Value = name;
        sheet.Cell(row, 2).Value = date.ToDateTime(TimeOnly.MinValue);
        sheet.Cell(row, 3).Value = kind;
        sheet.Cell(row, 4).Value = repeats ? "Yes" : "No";
        sheet.Row(row).Style.Font.FontColor = XLColor.FromHtml("#94A3B8");
        sheet.Row(row).Style.Font.Italic = true;
    }

    /// <summary>
    /// Reads the rows out of a filled-in template, saying of each one what it
    /// says rather than whether it can be saved: whether a holiday is already
    /// on the calendar is not a question a spreadsheet can answer.
    /// </summary>
    public static (HolidayImportFailure Failure, IReadOnlyList<ParsedRow> Rows) Read(Stream workbook)
    {
        XLWorkbook book;

        try
        {
            book = new XLWorkbook(workbook);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Anything from a PDF renamed .xlsx to a corrupt upload. The reader
            // throws several different types; none of them is worth telling an
            // admin apart.
            return (HolidayImportFailure.Unreadable, []);
        }

        using (book)
        {
            var sheet = book.Worksheets.FirstOrDefault(candidate => candidate.Name == HolidayTemplate.SheetName)
                ?? book.Worksheets.FirstOrDefault();

            if (sheet is null)
            {
                return (HolidayImportFailure.Unreadable, []);
            }

            if (!HeaderMatches(sheet))
            {
                return (HolidayImportFailure.NotTheTemplate, []);
            }

            var rows = new List<ParsedRow>();
            var last = sheet.LastRowUsed()?.RowNumber() ?? 1;

            if (last - 1 > HolidayTemplate.MostRows)
            {
                return (HolidayImportFailure.TooManyRows, []);
            }

            for (var row = 2; row <= last; row++)
            {
                var name = Text(sheet, row, 1);
                var date = Text(sheet, row, 2);
                var kind = Text(sheet, row, 3);
                var repeats = Text(sheet, row, 4);

                // A wholly empty row is a gap in the sheet, not a mistake in it.
                // Excel leaves plenty of them behind.
                if (name.Length == 0 && date.Length == 0 && kind.Length == 0 && repeats.Length == 0)
                {
                    continue;
                }

                rows.Add(Parse(sheet, row, name, kind, repeats));
            }

            return rows.Count == 0
                ? (HolidayImportFailure.Empty, [])
                : (HolidayImportFailure.None, rows);
        }
    }

    private static ParsedRow Parse(IXLWorksheet sheet, int row, string name, string kind, string repeats)
    {
        if (name.Length == 0)
        {
            return ParsedRow.Bad(row, null, null, "This row has no name.");
        }

        if (name.Length > 200)
        {
            return ParsedRow.Bad(row, name, null, "The name is longer than 200 characters.");
        }

        if (ReadDate(sheet.Cell(row, 2)) is not DateOnly date)
        {
            return ParsedRow.Bad(row, name, null, "The date could not be read. Use a date cell, or write it as 2026-12-25.");
        }

        if (!HolidayKind.IsSupported(kind))
        {
            return ParsedRow.Bad(
                row,
                name,
                date,
                $"\"{kind}\" is not a kind. Use {string.Join(" or ", HolidayKind.All)}.");
        }

        if (ReadBoolean(repeats) is not bool annual)
        {
            return ParsedRow.Bad(row, name, date, $"\"{repeats}\" is not Yes or No.");
        }

        return ParsedRow.Good(row, name, date, kind, annual);
    }

    private static bool HeaderMatches(IXLWorksheet sheet)
    {
        for (var column = 0; column < HolidayTemplate.Columns.Count; column++)
        {
            if (!string.Equals(
                    Text(sheet, 1, column + 1),
                    HolidayTemplate.Columns[column],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string Text(IXLWorksheet sheet, int row, int column) =>
        sheet.Cell(row, column).GetFormattedString().Trim();

    /// <summary>
    /// A real date cell first, then text. Excel hands back a date when the cell
    /// was formatted as one and a string when it was typed into a text column,
    /// and an admin filling in a template does both.
    /// </summary>
    private static DateOnly? ReadDate(IXLCell cell)
    {
        if (cell.DataType == XLDataType.DateTime && cell.TryGetValue<DateTime>(out var value))
        {
            return DateOnly.FromDateTime(value);
        }

        var text = cell.GetFormattedString().Trim();

        if (text.Length == 0)
        {
            return null;
        }

        // Invariant first, because that is what the template's own format
        // writes; then the machine's, for a sheet typed up locally.
        return DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)
                ? parsed
                : null;
    }

    private static bool? ReadBoolean(string text)
    {
        var value = text.Trim().ToLowerInvariant();

        if (Yes.Contains(value, StringComparer.Ordinal))
        {
            return true;
        }

        // Blank means no: a template row left untouched in that column is the
        // ordinary case, and a moving holiday is the one that needs saying.
        return value.Length == 0 || No.Contains(value, StringComparer.Ordinal) ? false : null;
    }

    /// <summary>One row as the sheet gave it, before the calendar is consulted.</summary>
    internal sealed record ParsedRow(
        int Row,
        string? Name,
        DateOnly? Date,
        string? Kind,
        bool RepeatsAnnually,
        string? Problem)
    {
        public bool IsUsable => Problem is null;

        public static ParsedRow Good(int row, string name, DateOnly date, string kind, bool repeats) =>
            new(row, name, date, kind, repeats, null);

        public static ParsedRow Bad(int row, string? name, DateOnly? date, string problem) =>
            new(row, name, date, null, false, problem);
    }
}
