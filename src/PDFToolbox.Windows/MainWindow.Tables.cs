using System.Data;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Almarfeld.PdfEngine;
using Microsoft.Win32;

namespace Almarfeld.PdfToolbox;

public partial class MainWindow
{
    private readonly StackPanel tableOptions = new() { Visibility = Visibility.Collapsed };
    private readonly Grid tablePreview = new() { Visibility = Visibility.Collapsed, Margin = new(0, 10, 0, 0) };
    private readonly TextBox tableRanges = new();
    private readonly ComboBox tableFormat = new(), tablePicker = new();
    private readonly CheckBox tableJoin = new() { Content = "Join matching consecutive-page tables" };
    private readonly CheckBox csvProtection = new() { Content = "Prefix formula-like CSV cells with an apostrophe", IsChecked = true };
    private readonly CheckBox tablesReviewed = new() { Content = "I reviewed the detected tables and warnings" };
    private readonly TextBlock tableStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly TextBlock tableDetail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly DataGrid tableGrid = new()
    {
        IsReadOnly = true, AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false,
        EnableRowVirtualization = true, EnableColumnVirtualization = true, HeadersVisibility = DataGridHeadersVisibility.Column,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = System.Windows.Media.Brushes.White, Foreground = System.Windows.Media.Brushes.Black,
        RowBackground = System.Windows.Media.Brushes.White, AlternatingRowBackground = System.Windows.Media.Brushes.WhiteSmoke,
    };
    private TableScan? tableScan;

    private void SetupTables()
    {
        var settings = (StackPanel)ToolHelp.Parent;
        settings.Children.Insert(settings.Children.IndexOf(ToolHelp) + 1, tableOptions);
        void Field(string label, Control control)
        { tableOptions.Children.Add(new TextBlock { Text = label }); AutomationProperties.SetName(control, label); tableOptions.Children.Add(control); }
        Field("Pages / ranges (blank = all pages)", tableRanges);
        tableRanges.TextChanged += (_, _) => ClearTableScan();
        tableOptions.Children.Add(tableJoin);
        tableJoin.ToolTip = "Heuristic: matching column positions on consecutive pages. Headers are retained. Review every joined table.";
        tableJoin.Checked += (_, _) => ClearTableScan(); tableJoin.Unchecked += (_, _) => ClearTableScan();
        var scan = new Button { Content = "Find tables" }; scan.Click += async (_, _) => await FindTables(); tableOptions.Children.Add(scan);
        tableFormat.Items.Add("Excel workbook (XLSX)"); tableFormat.Items.Add("CSV (one file per table)"); tableFormat.SelectedIndex = 0;
        Field("Export format", tableFormat); tableOptions.Children.Add(csvProtection);
        tableFormat.SelectionChanged += (_, _) => csvProtection.Visibility = tableFormat.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        csvProtection.Visibility = Visibility.Collapsed;
        tableOptions.Children.Add(new TextBlock { Text = "XLSX stores all cells as text, preserving identifiers and leading zeros. CSV import can still reinterpret values. Formula protection changes CSV cell text. Existing outputs are renamed, never replaced.", TextWrapping = TextWrapping.Wrap });
        tableOptions.Children.Add(tablesReviewed); tablesReviewed.IsEnabled = false;
        tablePreview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tablePreview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tablePreview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        tablePreview.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        tablePreview.Children.Add(tableStatus); Grid.SetRow(tablePicker, 1); tablePreview.Children.Add(tablePicker);
        Grid.SetRow(tableDetail, 2); tablePreview.Children.Add(tableDetail); Grid.SetRow(tableGrid, 3); tablePreview.Children.Add(tableGrid);
        AutomationProperties.SetName(tableGrid, "Detected table preview"); AutomationProperties.SetName(tablePicker, "Choose detected table");
        tablePicker.SelectionChanged += (_, _) => ShowTable();
        ((Grid)PagesList.Parent).Children.Add(tablePreview); Grid.SetRow(tablePreview, 1); Grid.SetRowSpan(tablePreview, 2);
    }

    private void SetTableTool(string value)
    {
        tableOptions.Visibility = tablePreview.Visibility = value == "Tables" ? Visibility.Visible : Visibility.Collapsed;
        CollisionBox.IsEnabled = value != "Tables";
        if (value == "Tables")
        {
            PagesList.Visibility = Visibility.Collapsed;
            ToolHelp.Text = "Read tables from selectable PDF text. No OCR: scanned pages are reported, not guessed. Layout detection is approximate; review columns, missing pages and wrapped rows before exporting. Only the selected PDF is processed.";
        }
        ClearTableScan();
    }
    private void ClearTableScan()
    {
        tableScan = null; tableGrid.ItemsSource = null; tablePicker.Items.Clear(); tableDetail.Text = "";
        tableStatus.Text = "Choose a PDF, then Find tables."; tablesReviewed.IsChecked = false; tablesReviewed.IsEnabled = false;
    }
    private async Task FindTables()
    {
        if (vm.Busy) return;
        var file = FilesList.SelectedItem as FileItem ?? vm.Files.FirstOrDefault();
        if (file?.Info == null) { Error(new PdfOperationException("Empty", "Open a PDF first.")); return; }
        try
        {
            var pages = string.IsNullOrWhiteSpace(tableRanges.Text) ? Enumerable.Range(1, file.Info.PageCount).ToArray() : PageRanges.Parse(tableRanges.Text, file.Info.PageCount).ToArray();
            var join = tableJoin.IsChecked == true;
            ClearError(); ClearTableScan(); vm.Busy = true; operation = new(); vm.Progress = 0;
            var progress = new Progress<JobProgress>(p => { vm.Progress = 100d * p.Completed / Math.Max(1, p.Total); vm.Status = $"{p.Stage}: {p.Completed}/{p.Total}"; });
            tableScan = await engine.ScanTablesAsync(file.Input, pages, join, progress, operation.Token);
            foreach (var table in tableScan.Tables)
                tablePicker.Items.Add($"Table {tablePicker.Items.Count + 1} · pages {string.Join(", ", table.Pages)} · {table.Rows.Length} rows × {table.ColumnCount} columns");
            tableStatus.Text = $"{tableScan.Tables.Count} tables detected. Layout is inferred; verify against the PDF.";
            if (tableScan.PagesWithoutText.Length > 0)
                tableStatus.Text += " Pages with insufficient selectable text: " + string.Join(", ", tableScan.PagesWithoutText) + ". OCR is not available.";
            if (tableScan.Tables.Count == 0) tableStatus.Text += " No export is available. Try a text-based PDF with clearly separated columns.";
            tablesReviewed.IsEnabled = tableScan.Tables.Count > 0;
            if (tablePicker.Items.Count > 0) tablePicker.SelectedIndex = 0;
            vm.Status = "Scan complete. Review the preview; originals are unchanged.";
        }
        catch (Exception e) { ClearTableScan(); Error(e); }
        finally { vm.Busy = false; operation?.Dispose(); operation = null; }
    }
    private void ShowTable()
    {
        tableGrid.ItemsSource = null;
        if (tableScan == null || tablePicker.SelectedIndex < 0 || tablePicker.SelectedIndex >= tableScan.Tables.Count) return;
        var table = tableScan.Tables[tablePicker.SelectedIndex]; var data = new DataTable();
        for (var c = 0; c < table.ColumnCount; c++) data.Columns.Add($"Column {c + 1}", typeof(string));
        foreach (var row in table.Rows.Take(200)) data.Rows.Add(row.Cast<object>().ToArray());
        tableGrid.ItemsSource = data.DefaultView;
        tableDetail.Text = $"Preview: {Math.Min(200, table.Rows.Length)} of {table.Rows.Length} rows. Export includes every detected row, including headers.";
        if (table.NeedsReview) tableDetail.Text += " Ambiguous/wrapped rows (1-based): " + string.Join(", ", table.UncertainRows.Take(50).Select(i => i + 1)) + (table.UncertainRows.Length > 50 ? " …" : "");
    }
    private async Task ExportTables()
    {
        if (tableScan is not { Tables.Count: > 0 } scan || tablesReviewed.IsChecked != true)
        { Error(new PdfOperationException("Review", "Find tables, review every preview and acknowledge the warnings before exporting.")); return; }
        var folder = new OpenFolderDialog { Title = "Choose spreadsheet output folder" };
        if (folder.ShowDialog(this) != true) return;
        var xlsx = tableFormat.SelectedIndex == 0; var protect = csvProtection.IsChecked == true;
        var sources = vm.Files.Select(f => f.Path).ToArray();
        ClearError(); vm.Busy = true; operation = new(); vm.Progress = 0; vm.Results.Clear(); ResultsPanel.Visibility = Visibility.Visible;
        try
        {
            var progress = new Progress<JobProgress>(p => { vm.Progress = 100d * p.Completed / Math.Max(1, p.Total); vm.Status = $"{p.Stage}: {p.Completed}/{p.Total}"; });
            var completed = new Progress<OutputResult>(result => vm.Results.Add(result));
            await engine.SaveTablesAsync(scan, xlsx, folder.FolderName, protect, sources, progress, operation.Token, completed);
            vm.Status = $"Table export: {vm.Results.Count(r => r.Status == "Success")} succeeded, {vm.Results.Count(r => r.Status != "Success")} failed. Originals unchanged.";
        }
        catch (Exception e) { Error(e); }
        finally { vm.Busy = false; operation?.Dispose(); operation = null; }
    }
}
