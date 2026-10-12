using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Almarfeld.PdfEngine;
using Microsoft.Win32;

namespace Almarfeld.PdfToolbox;

public partial class MainWindow
{
    private StackPanel featureOptions = null!;
    private Grid featurePreview = null!;
    private readonly Image overlayPreview = new() { Stretch = System.Windows.Media.Stretch.Uniform, Margin = new(12) };
    private readonly TextBlock previewMessage = new() { Text = "Choose a PDF and preview page.", Margin = new(8) };
    private readonly TextBox previewPage = new() { Text = "1", Width = 70 };
    private readonly ComboBox selectionMode = new(), rasterFormat = new(), watermarkKind = new(), overlayPosition = new(), numberFormat = new();
    private readonly TextBox featureRanges = new() { Text = "1" }, dpi = new() { Text = "144" }, rasterQuality = new() { Text = "90" }, rasterPrefix = new() { Text = "document" };
    private readonly TextBox markText = new() { Text = "DRAFT" }, markFont = new() { Text = "48" }, markOpacity = new() { Text = "0.3" }, markAngle = new() { Text = "-30" },
        offsetX = new() { Text = "0" }, offsetY = new() { Text = "0" }, markScale = new() { Text = "0.3" };
    private readonly TextBlock imageName = new() { Text = "No watermark image selected" };
    private string? watermarkPath;
    private readonly TextBox numberStart = new() { Text = "1" }, numberPrefix = new(), numberSuffix = new(), numberFont = new() { Text = "12" },
        numberMargin = new() { Text = "24" }, numberSkip = new() { Text = "0" };
    private readonly StackPanel rasterSettings = new(), watermarkSettings = new(), numberSettings = new();
    private readonly StackPanel textMarkSettings = new(), imageMarkSettings = new(), positionSettings = new();
    private readonly Button updatePreviewButton = new() { Content = "Update preview" };
    private readonly DispatcherTimer previewTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private CancellationTokenSource featurePreviewCancellation = new();
    private bool featureReady;

    private void SetupFeatures()
    {
        var settingsParent = (StackPanel)ToolHelp.Parent;
        AutomationProperties.SetName(overlayPreview, "PDF export preview");
        featureOptions = new StackPanel { Visibility = Visibility.Collapsed };
        settingsParent.Children.Insert(settingsParent.Children.IndexOf(ToolHelp) + 1, featureOptions);
        void Field(StackPanel panel, string label, Control control)
        {
            panel.Children.Add(new TextBlock { Text = label }); AutomationProperties.SetName(control, label); panel.Children.Add(control);
            if (control is TextBox text) text.TextChanged += (_, _) => QueueFeaturePreview();
            if (control is ComboBox combo) combo.SelectionChanged += (_, _) => QueueFeaturePreview();
        }
        static void Choices(ComboBox box, params string[] values) { foreach (var value in values) box.Items.Add(value); box.SelectedIndex = 0; }
        Choices(selectionMode, "All pages", "Pages / ranges", "Highlighted pages (image export)");
        Field(featureOptions, "Apply to", selectionMode); Field(featureOptions, "Page ranges (example: 1-3,8)", featureRanges);
        Choices(rasterFormat, "PNG", "JPEG"); Field(rasterSettings, "Image format", rasterFormat); Field(rasterSettings, "Resolution / DPI (36–600)", dpi);
        Field(rasterSettings, "JPEG quality (10–100)", rasterQuality); Field(rasterSettings, "Filename prefix", rasterPrefix);
        rasterSettings.Children.Add(new TextBlock { Text = "Names: prefix-page-001.png (source page number). One page is rendered at a time. Export is limited to 40 MP per page." });
        featureOptions.Children.Add(rasterSettings);
        Choices(watermarkKind, "Text", "Image (PNG / JPEG)"); Field(watermarkSettings, "Watermark type", watermarkKind);
        Field(textMarkSettings, "Watermark text", markText); Field(textMarkSettings, "Text size (points)", markFont);
        watermarkSettings.Children.Add(textMarkSettings);
        var chooseImage = new Button { Content = "Choose watermark image" }; AutomationProperties.SetName(chooseImage, "Choose watermark image");
        chooseImage.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "PNG / JPEG|*.png;*.jpg;*.jpeg", CheckFileExists = true }; if (dialog.ShowDialog(this) == true) { watermarkPath = dialog.FileName; imageName.Text = Path.GetFileName(watermarkPath); QueueFeaturePreview(); } };
        imageMarkSettings.Children.Add(chooseImage); imageMarkSettings.Children.Add(imageName);
        Field(imageMarkSettings, "Image width / displayed page width (0.01–2)", markScale);
        watermarkSettings.Children.Add(imageMarkSettings);
        watermarkKind.SelectionChanged += (_, _) => { textMarkSettings.Visibility = watermarkKind.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed; imageMarkSettings.Visibility = watermarkKind.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed; };
        imageMarkSettings.Visibility = Visibility.Collapsed;
        Field(watermarkSettings, "Opacity (0–1)", markOpacity); Field(watermarkSettings, "Clockwise rotation (degrees)", markAngle);
        Field(watermarkSettings, "Horizontal offset (points; right is positive)", offsetX); Field(watermarkSettings, "Vertical offset (points; down is positive)", offsetY);
        featureOptions.Children.Add(watermarkSettings);
        Choices(numberFormat, "1", "Page 1", "1 / 10", "Page 1 of 10"); Field(numberSettings, "Number format", numberFormat);
        Field(numberSettings, "Starting number", numberStart); Field(numberSettings, "Prefix", numberPrefix); Field(numberSettings, "Suffix", numberSuffix);
        Field(numberSettings, "Number font size (points)", numberFont); Field(numberSettings, "Margin (points)", numberMargin); Field(numberSettings, "Exclude first N source pages", numberSkip);
        numberSettings.Children.Add(new TextBlock { Text = "Numbers follow selected pages in document order. The total is the last assigned number, including the starting offset." });
        featureOptions.Children.Add(numberSettings);
        Field(positionSettings, "Overlay position", overlayPosition); featureOptions.Children.Add(positionSettings);
        updatePreviewButton.Click += (_, _) => QueueFeaturePreview(); featureOptions.Children.Add(updatePreviewButton);
        selectionMode.SelectionChanged += (_, _) => featureRanges.IsEnabled = selectionMode.SelectedIndex == 1;
        featureRanges.IsEnabled = false;
        featurePreview = new Grid { Visibility = Visibility.Collapsed };
        featurePreview.RowDefinitions.Add(new() { Height = GridLength.Auto }); featurePreview.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        featurePreview.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar.Children.Add(new TextBlock { Text = "Preview page", VerticalAlignment = VerticalAlignment.Center, Margin = new(8) });
        AutomationProperties.SetName(previewPage, "Preview page number"); toolbar.Children.Add(previewPage);
        previewPage.TextChanged += (_, _) => QueueFeaturePreview();
        featurePreview.Children.Add(toolbar); Grid.SetRow(overlayPreview, 1); featurePreview.Children.Add(overlayPreview); Grid.SetRow(previewMessage, 2); featurePreview.Children.Add(previewMessage);
        ((Grid)PagesList.Parent).Children.Add(featurePreview); Grid.SetRow(featurePreview, 1);
        previewTimer.Tick += async (_, _) => { previewTimer.Stop(); await UpdateFeaturePreview(); };
        featureReady = true;
    }
    private void SetFeatureTool(string value)
    {
        featurePreviewCancellation.Cancel(); overlayPreview.Source = null; previewTimer.Stop();
        var feature = value is "Raster" or "Watermark" or "Numbers";
        FileQueueRow.Height = new GridLength(ActualHeight < 620 ? 54 : ActualHeight < 800 || value is "Watermark" or "Numbers" ? 76 : 150);
        featureOptions.Visibility = feature ? Visibility.Visible : Visibility.Collapsed;
        rasterSettings.Visibility = value == "Raster" ? Visibility.Visible : Visibility.Collapsed;
        watermarkSettings.Visibility = value == "Watermark" ? Visibility.Visible : Visibility.Collapsed;
        numberSettings.Visibility = value == "Numbers" ? Visibility.Visible : Visibility.Collapsed;
        positionSettings.Visibility = updatePreviewButton.Visibility = value is "Watermark" or "Numbers" ? Visibility.Visible : Visibility.Collapsed;
        if (value is "Watermark" or "Numbers" && selectionMode.SelectedIndex == 2) selectionMode.SelectedIndex = 0;
        featurePreview.Visibility = value is "Watermark" or "Numbers" ? Visibility.Visible : Visibility.Collapsed;
        if (value is "Watermark" or "Numbers") PagesList.Visibility = Visibility.Collapsed;
        overlayPosition.Items.Clear();
        foreach (var p in Enum.GetValues<OverlayPosition>().Where(p => value != "Numbers" || p is not (OverlayPosition.Center or OverlayPosition.CenterLeft or OverlayPosition.CenterRight))) overlayPosition.Items.Add(p);
        overlayPosition.SelectedItem = value == "Numbers" ? OverlayPosition.BottomCenter : OverlayPosition.Center;
        if (feature) ToolHelp.Text = value switch { "Raster" => "Render actual PDF pages as PNG or JPEG. The source is unchanged. Review per-page results if cancelled or a write fails.", "Watermark" => "Add text or an image as PDF content. Preview uses the same drawing code as export; files remain unchanged until you export a copy.", _ => "Add sequential numbers to selected pages in a new copy. Existing page dimensions and rotations are retained." };
        QueueFeaturePreview();
    }
    private void QueueFeaturePreview()
    {
        if (!featureReady || tool is not ("Watermark" or "Numbers")) return;
        featurePreviewCancellation.Cancel(); previewTimer.Stop(); previewTimer.Start();
        overlayPreview.Source = null; previewMessage.Text = "Preview pending…";
    }
    private static double Decimal(TextBox box)
    {
        if (!double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value)) throw new PdfOperationException("Options", "Enter a valid number using a decimal point.");
        return value;
    }
    private static int Integer(TextBox box)
    {
        if (!int.TryParse(box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw new PdfOperationException("Options", "Enter a non-negative whole number."); return value;
    }
    private int[] FeaturePages(FileItem file)
    {
        if (selectionMode.SelectedIndex == 0) return Enumerable.Range(1, file.Info!.PageCount).ToArray();
        if (selectionMode.SelectedIndex == 1) return PageRanges.Parse(featureRanges.Text, file.Info!.PageCount).ToArray();
        if (tool != "Raster") throw new PdfOperationException("Selection", "For overlays choose All pages or enter page ranges.");
        var picks = SelectedPages().Select(p => p.Page).ToArray();
        if (picks.Length == 0) throw new PdfOperationException("Selection", "Highlight pages in the page list first."); return picks;
    }
    private WatermarkOptions MarkOptions()
    {
        if (watermarkKind.SelectedIndex == 1 && watermarkPath == null) throw new PdfOperationException("Watermark", "Choose a PNG/JPEG watermark image.");
        var options = new WatermarkOptions(markText.Text, watermarkKind.SelectedIndex == 1 ? watermarkPath : null, Decimal(markFont), Decimal(markOpacity), Decimal(markAngle),
            (OverlayPosition)(overlayPosition.SelectedItem ?? OverlayPosition.Center), Decimal(offsetX), Decimal(offsetY), Decimal(markScale));
        PdfService.ValidateWatermark(options); return options;
    }
    private NumberOptions NumberSettings()
    {
        var options = new NumberOptions(Integer(numberStart), numberPrefix.Text, numberSuffix.Text, (NumberFormat)numberFormat.SelectedIndex, Decimal(numberFont), Decimal(numberMargin),
            (OverlayPosition)(overlayPosition.SelectedItem ?? OverlayPosition.BottomCenter), Integer(numberSkip)); PdfService.ValidateNumbers(options); return options;
    }
    private async Task UpdateFeaturePreview()
    {
        if (vm.Busy || tool is not ("Watermark" or "Numbers")) return;
        featurePreviewCancellation.Dispose(); featurePreviewCancellation = new(); var token = featurePreviewCancellation.Token;
        try
        {
            var file = FilesList.SelectedItem as FileItem ?? vm.Files.FirstOrDefault(); if (file?.Info == null) { previewMessage.Text = "Open a PDF to preview."; return; }
            var page = Integer(previewPage); var pages = FeaturePages(file);
            var mark = tool == "Watermark" ? MarkOptions() : null; var numbers = tool == "Numbers" ? NumberSettings() : null;
            // Include the image version in the variant so replacing that image does not reuse an old preview.
            var imageVersion = mark?.ImagePath is { } path ? $"{new FileInfo(path).LastWriteTimeUtc.Ticks}|{new FileInfo(path).Length}" : "";
            var key = $"{mark}|{numbers}|{string.Join(',', pages)}|{imageVersion}";
            var bitmap = await thumbnails.PreviewAsync(file.Input, page, 900, key, ct => engine.OverlayPreviewAsync(file.Input, page, pages, mark, numbers, ct), token);
            if (!token.IsCancellationRequested) { overlayPreview.Source = bitmap; previewMessage.Text = $"Page {page} · export preview · original unchanged"; }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) { if (!token.IsCancellationRequested) { overlayPreview.Source = null; previewMessage.Text = "Preview needs attention: " + exception.Message; } }
    }
    private async Task ExportFeature(FileItem file)
    {
        try
        {
            var pages = FeaturePages(file); var mark = tool == "Watermark" ? MarkOptions() : null; var numbers = tool == "Numbers" ? NumberSettings() : null;
            var raster = tool == "Raster" ? new RasterOptions((RasterFormat)rasterFormat.SelectedIndex, Integer(dpi), Integer(rasterQuality), rasterPrefix.Text) : new(); if (tool == "Raster") PdfService.ValidateRaster(raster);
            var policy = (CollisionPolicy)new[] { 1, 0, 2 }[CollisionBox.SelectedIndex];
            string output; HashSet<string>? approved = null;
            if (tool == "Raster")
            {
                var dialog = new OpenFolderDialog { Title = "Choose image output folder" }; if (dialog.ShowDialog(this) != true) return; output = dialog.FolderName;
                if (policy == CollisionPolicy.Replace)
                {
                    approved = pages.Select(p => Path.GetFullPath(Path.Combine(output, PdfService.RasterName(raster, p)))).Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (approved.Count > 0 && MessageBox.Show(this, $"Replace {approved.Count} existing OUTPUT images?\n\n" + string.Join('\n', approved.Take(6)) + "\n\nSource files are protected.", "Confirm image replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                }
            }
            else
            {
                var save = new SaveFileDialog { Filter = "PDF document|*.pdf", DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = false, FileName = Path.GetFileNameWithoutExtension(file.Path) + "-" + tool.ToLowerInvariant() + ".pdf" };
                if (save.ShowDialog(this) != true) return; output = save.FileName;
                if (policy == CollisionPolicy.Replace)
                {
                    if (!File.Exists(output)) policy = CollisionPolicy.Cancel;
                    else if (MessageBox.Show(this, "Replace this existing OUTPUT PDF?\n\n" + output, "Confirm output replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                }
            }
            featurePreviewCancellation.Cancel(); vm.Busy = true; operation = new(); vm.Progress = 0; vm.Status = "Preparing export…";
            vm.Results.Clear(); ResultsPanel.Visibility = Visibility.Visible;
            var progress = new Progress<JobProgress>(p => { vm.Progress = 100d * p.Completed / Math.Max(1, p.Total); vm.Status = p.Stage; });
            if (tool == "Raster") foreach (var result in await engine.RasterAsync(file.Input, pages, raster, output, policy, progress, operation.Token, approved)) vm.Results.Add(result);
            else vm.Results.Add(mark != null ? await engine.WatermarkAsync(file.Input, pages, mark, output, policy, progress, operation.Token) : await engine.NumbersAsync(file.Input, pages, numbers!, output, policy, progress, operation.Token));
            vm.Status = $"Export finished: {vm.Results.Count(r => r.Status == "Success")} succeeded, {vm.Results.Count(r => r.Status != "Success")} failed/skipped. Sources unchanged.";
        }
        catch (Exception exception) { Error(exception); }
        finally { vm.Busy = false; operation?.Dispose(); operation = null; QueueFeaturePreview(); }
    }
}
