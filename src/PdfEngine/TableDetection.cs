using System.Text;

namespace Almarfeld.PdfEngine;

public sealed record TableGlyph(string Text, double X, double Y, double Width, double FontSize);
public sealed record ExtractedTable(int[] Pages, string[][] Rows, int[] UncertainRows, double[] ColumnCenters)
{
    public int ColumnCount => ColumnCenters.Length;
    public bool NeedsReview => UncertainRows.Length > 0;
}
public sealed record TableScan(string SourcePath, IReadOnlyList<ExtractedTable> Tables, int[] PagesWithoutText);

/// <summary>Position-based text table detection, adapted from the Android tool. No OCR.</summary>
public static class TableDetection
{
    private sealed record Cell(string Text, double Left, double Right);
    private sealed record Line(double Y, double Height, List<Cell> Cells);

    public static IReadOnlyList<ExtractedTable> Detect(int page, IReadOnlyList<TableGlyph> glyphs)
    {
        var groups = new List<List<TableGlyph>>(); double baselineSum = 0;
        foreach (var glyph in glyphs.Where(g => !string.IsNullOrWhiteSpace(g.Text) && g.Width >= 0)
                     .OrderBy(g => g.Y).ThenBy(g => g.X))
        {
            var last = groups.LastOrDefault();
            if (last != null && Math.Abs(glyph.Y - baselineSum / last.Count) <= Math.Max(glyph.FontSize, 1) * .45)
            { last.Add(glyph); baselineSum += glyph.Y; }
            else { groups.Add([glyph]); baselineSum = glyph.Y; }
        }
        var lines = groups.Select(group =>
        {
            var ordered = group.OrderBy(g => g.X).ToArray();
            var height = ordered.Select(g => Math.Max(1, g.FontSize)).Order().ElementAt(ordered.Length / 2);
            var cells = new List<Cell>(); var text = new StringBuilder();
            var left = ordered[0].X; var right = left;
            foreach (var glyph in ordered)
            {
                var gap = glyph.X - right;
                if (text.Length > 0 && gap >= .8 * height)
                {
                    cells.Add(new(text.ToString().Trim(), left, right)); text.Clear(); left = glyph.X;
                }
                else if (text.Length > 0 && gap >= .12 * height && text[^1] != ' ') text.Append(' ');
                text.Append(glyph.Text); right = Math.Max(right, glyph.X + glyph.Width);
            }
            if (text.Length > 0) cells.Add(new(text.ToString().Trim(), left, right));
            return new Line(group.Average(g => g.Y), height, cells);
        }).ToArray();
        var result = new List<ExtractedTable>(); var region = new List<Line>();
        void Finish()
        {
            while (region.Count > 0 && region[^1].Cells.Count < 2) region.RemoveAt(region.Count - 1);
            if (region.Count(l => l.Cells.Count >= 2) >= 2 && Build(page, region) is { } table) result.Add(table);
            region.Clear();
        }
        foreach (var line in lines)
        {
            var prior = region.LastOrDefault();
            var gap = prior == null ? 0 : (line.Y - prior.Y) / prior.Height;
            if (line.Cells.Count >= 2) { if (prior != null && gap > 3) Finish(); region.Add(line); }
            else if (prior != null && gap <= 1.6) region.Add(line);
            else Finish();
        }
        Finish(); return result;
    }

    private static ExtractedTable? Build(int page, List<Line> region)
    {
        var bands = new List<(double Left, double Right)>();
        foreach (var cell in region.Where(l => l.Cells.Count >= 2).SelectMany(l => l.Cells).OrderBy(c => c.Right - c.Left))
        {
            var touching = Enumerable.Range(0, bands.Count).Where(i => bands[i].Left < cell.Right && cell.Left < bands[i].Right).ToArray();
            if (touching.Length == 0)
            {
                bands.Add((cell.Left, cell.Right));
                if (bands.Count > 256) throw new PdfOperationException("Limit", "The detected layout exceeds 256 columns. Select a simpler page range.");
            }
            else if (touching.Length == 1)
            {
                var i = touching[0]; bands[i] = (Math.Min(bands[i].Left, cell.Left), Math.Max(bands[i].Right, cell.Right));
            }
        }
        bands.Sort((a, b) => a.Left.CompareTo(b.Left));
        for (var i = 0; i + 1 < bands.Count;)
            if (bands[i].Right > bands[i + 1].Left)
            { bands[i] = (bands[i].Left, Math.Max(bands[i].Right, bands[i + 1].Right)); bands.RemoveAt(i + 1); }
            else i++;
        if (bands.Count < 2) return null;
        if (bands.Count > 256) throw new PdfOperationException("Limit", "A detected table exceeds 256 columns. Select a smaller page range.");
        var bounds = new[] { double.NegativeInfinity }.Concat(Enumerable.Range(0, bands.Count - 1)
            .Select(i => (bands[i].Right + bands[i + 1].Left) / 2)).Append(double.PositiveInfinity).ToArray();
        var rows = new List<string[]>(); var uncertain = new HashSet<int>(); Line? previous = null;
        foreach (var line in region)
        {
            var placed = line.Cells.Select(cell =>
            {
                var overlaps = Enumerable.Range(0, bands.Count).Select(i => Math.Max(0,
                    Math.Min(cell.Right, bounds[i + 1]) - Math.Max(cell.Left, bounds[i]))).ToArray();
                var column = Enumerable.Range(0, overlaps.Length).MaxBy(i => overlaps[i]);
                var spanning = overlaps.Count(v => v / Math.Max(.1, cell.Right - cell.Left) >= .3) > 1;
                return (Cell: cell, Column: column, Spanning: spanning);
            }).ToArray();
            if (placed.Length == 1 && rows.Count > 0 && previous != null &&
                (line.Y - previous.Y) / previous.Height <= 1.6 && rows[^1][placed[0].Column].Length > 0)
            {
                rows[^1][placed[0].Column] += "\n" + placed[0].Cell.Text;
                uncertain.Add(rows.Count - 1); // Wrapped text grouping is inferred, so flag it.
            }
            else
            {
                var row = Enumerable.Repeat("", bands.Count).ToArray(); var review = line.Cells.Count != bands.Count;
                foreach (var cell in placed)
                {
                    if (row[cell.Column].Length > 0) { row[cell.Column] += "\n"; review = true; }
                    row[cell.Column] += cell.Cell.Text; review |= cell.Spanning;
                }
                rows.Add(row); if (review) uncertain.Add(rows.Count - 1);
            }
            previous = line;
        }
        return new([page], rows.ToArray(), uncertain.Order().ToArray(), bands.Select(b => (b.Left + b.Right) / 2).ToArray());
    }

    // Explicit opt-in only. Never silently discard repeated headers or merge separate same-page tables.
    public static IReadOnlyList<ExtractedTable> JoinConsecutive(IReadOnlyList<ExtractedTable> tables)
    {
        var result = new List<ExtractedTable>();
        foreach (var table in tables)
        {
            var prior = result.LastOrDefault();
            if (prior != null && table.Pages[0] == prior.Pages[^1] + 1 && prior.ColumnCount == table.ColumnCount &&
                prior.ColumnCenters.Zip(table.ColumnCenters).All(p => Math.Abs(p.First - p.Second) <= 18))
            {
                result[^1] = prior with { Pages = prior.Pages.Concat(table.Pages).ToArray(), Rows = prior.Rows.Concat(table.Rows).ToArray(),
                    UncertainRows = prior.UncertainRows.Concat(Enumerable.Range(prior.Rows.Length, table.Rows.Length)).ToArray() };
            }
            else result.Add(table);
        }
        return result;
    }
}
