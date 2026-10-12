using System.Globalization;

namespace Almarfeld.PdfEngine;

public static class PageRanges
{
    public static IReadOnlyList<int> Parse(string text, int count, bool allowDuplicates = false)
    {
        if (count < 1 || string.IsNullOrWhiteSpace(text)) throw new PdfOperationException("Range", "Enter at least one page or page range.");
        var result = new List<int>();
        var seen = new HashSet<int>();
        foreach (var token in text.Split(','))
        {
            var parts = token.Trim().Split('-');
            if (parts.Length is < 1 or > 2 || parts.Any(x => !int.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw new PdfOperationException("Range", "Use page numbers and ascending ranges, such as 1-3,8,10-12.");
            var start = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
            var end = parts.Length == 2 ? int.Parse(parts[1].Trim(), CultureInfo.InvariantCulture) : start;
            if (start < 1 || end > count || end < start) throw new PdfOperationException("Range", $"Pages must be between 1 and {count}; descending ranges are not accepted.");
            for (var page = start; page <= end; page++)
            {
                if (!allowDuplicates && !seen.Add(page)) throw new PdfOperationException("Range", $"Page {page} is selected more than once.");
                result.Add(page);
                if (result.Count > PdfService.MaxOutputPages) throw new PdfOperationException("Limit", "Too many output pages.");
            }
        }
        return result;
    }

    public static IReadOnlyList<IReadOnlyList<int>> Split(SplitMode mode, string ranges, int count, int everyN)
    {
        if (count < 1) throw new PdfOperationException("Empty", "The PDF has no pages.");
        if (mode == SplitMode.Ranges) return ranges.Split(';').Select(x => Parse(x, count)).ToList();
        if (mode == SplitMode.SelectedPages) return Parse(ranges, count).Select(x => (IReadOnlyList<int>)new[] { x }).ToList();
        var n = mode == SplitMode.EveryPage ? 1 : everyN;
        if (n < 1 || n > PdfService.MaxOutputPages) throw new PdfOperationException("Range", "Pages per part must be a positive number within the supported limit.");
        return Enumerable.Range(1, count).Chunk(n).Select(x => (IReadOnlyList<int>)x).ToList();
    }
}
