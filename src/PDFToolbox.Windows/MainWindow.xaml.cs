using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Almarfeld.PdfEngine;
using Microsoft.Win32;

namespace Almarfeld.PdfToolbox;

public partial class MainWindow : Window
{
    private readonly Workspace vm = new();
    private readonly PdfService engine = new();
    private readonly ThumbnailService thumbnails = new();
    private readonly Stack<PageSelection[]> undo = new();
    private CancellationTokenSource? operation;
    private CancellationTokenSource previewCancellation = new();
    private string tool = "Home";
    private bool light, changing;
    private Point dragStart;
    private object? draggedItem;

    public MainWindow()
    {
        InitializeComponent(); DataContext = vm;
        SizeChanged += (_, _) => FileQueueRow.Height = new GridLength(ActualHeight < 800 ? 76 : 150);
        Loaded += async (_, _) =>
        {
            var paths = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (paths.Length > 0) await OpenPaths(paths);
        };
    }
    private void Error(Exception error)
    {
        ErrorBanner.Visibility = Visibility.Visible;
        ErrorText.Text = error switch
        {
            OperationCanceledException => "Operation cancelled. Sources are unchanged; any completed outputs remain listed below.",
            UnauthorizedAccessException => "Access was denied. Choose a readable source and a writable output folder.",
            IOException => "The file could not be read or written. It may be locked, unavailable or already exist. Sources are unchanged. " + error.Message,
            PdfOperationException => error.Message,
            _ => "The operation could not be completed. The PDF/image may be unsupported; sources are unchanged."
        };
        vm.Status = "Operation needs attention; no source was changed.";
    }
    private void ClearError() => ErrorBanner.Visibility = Visibility.Collapsed;
    private void SetTool(string value)
    {
        if (vm.Busy) return;
        if ((value == "Images") != (tool == "Images") && vm.Files.Count > 0)
        {
            if (MessageBox.Show(this, "Switching between PDFs and images clears the current workspace, without changing any files. Continue?", "Change workspace", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            ClearFiles();
        }
        tool = value; HomePanel.Visibility = value == "Home" ? Visibility.Visible : Visibility.Collapsed;
        WorkspacePanel.Visibility = value == "Home" ? Visibility.Collapsed : Visibility.Visible;
        Heading.Text = value switch { "Home" => "PDF tools that stay on your device", "Merge" => "Merge PDFs", "Split" => "Split PDF", "Organize" => "Organize Pages", "Extract" => "Extract Pages", "Rotate" => "Rotate Pages", "Images" => "Images to PDF", _ => "Document metadata" };
        Subheading.Text = value == "Home" ? "Open. Arrange. Export a copy. Your original files stay intact." : "Local processing · review your choices · export a new copy";
        RangePanel.Visibility = value is "Split" or "Extract" or "Rotate" ? Visibility.Visible : Visibility.Collapsed;
        SplitOptions.Visibility = value == "Split" ? Visibility.Visible : Visibility.Collapsed;
        ExtractOptions.Visibility = value == "Extract" ? Visibility.Visible : Visibility.Collapsed;
        RotateOptions.Visibility = value == "Rotate" ? Visibility.Visible : Visibility.Collapsed;
        ImageOptionsPanel.Visibility = value == "Images" ? Visibility.Visible : Visibility.Collapsed;
        MetadataPanel.Visibility = value == "Metadata" ? Visibility.Visible : Visibility.Collapsed;
        PageActions.Visibility = value == "Organize" ? Visibility.Visible : Visibility.Collapsed;
        PagesList.Visibility = value is "Images" or "Metadata" ? Visibility.Collapsed : Visibility.Visible;
        RangeHelp.Text = value == "Split" ? "Ranges: 1-3;4-6 creates two parts. Commas combine pages within a part." : "Example: 1-3,8,10-12";
        ToolHelp.Text = value switch
        {
            "Merge" => "Files are merged in the order shown. Drag files or use File ↑ / ↓. All pages of every file are included.",
            "Split" => "Choose ranges, every page, every N pages or selected pages. Output names are base-part-001.pdf, etc. Completed parts are kept and individually reported if another part fails.",
            "Organize" => "Select pages (Ctrl/Shift for several). Drag to reorder or use the buttons. Rotation, duplication and deletion affect only this export plan. Undo restores the previous plan.",
            "Extract" => "Export selected ranges from the selected source. Optionally make one PDF per selected page.",
            "Rotate" => "Apply clockwise rotation to all pages or the specified ranges. Existing source rotations are retained.",
            "Images" => "JPG/JPEG, PNG and BMP. Reorder files before export. Fit preserves the whole image; Fill deliberately crops edges. The selected source image is previewed below.",
            _ => "Read and edit document information in a new copy. Producer is controlled by the PDF library. Existing XMP, attachments and page content are not sanitized."
        };
        ClearError(); RefreshSelected();
    }
    private void ToolClick(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private async void OpenClick(object sender, RoutedEventArgs e)
    {
        if (vm.Busy) return;
        var dialog = new OpenFileDialog { Multiselect = true, CheckFileExists = true, Filter = tool == "Images" ? "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" : "PDF files (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) == true) await OpenPaths(dialog.FileNames);
    }
    private async Task OpenPaths(string[] paths)
    {
        if (vm.Busy || paths.Length == 0) return;
        if (paths.Length + vm.Files.Count > 100) { Error(new PdfOperationException("Limit", "The workspace supports up to 100 input files.")); return; }
        if (tool == "Home") SetTool(paths.Length > 1 ? "Merge" : "Organize");
        vm.Busy = true; ClearError(); operation = new(); vm.Progress = 0;
        try
        {
            for (var i = 0; i < paths.Length; i++)
            {
                operation.Token.ThrowIfCancellationRequested(); var path = Path.GetFullPath(paths[i]);
                if (vm.Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                vm.Status = $"Opening {i + 1} of {paths.Length}…";
                PdfInfo? info = null; var input = new PdfInput(path);
                if (tool != "Images")
                {
                    if (!Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) throw new PdfOperationException("Input", "Choose PDF files for this tool.");
                    try { info = await engine.InspectAsync(input, operation.Token); }
                    catch (PdfOperationException exception) when (exception.Code == "Password")
                    {
                        var password = AskPassword();
                        if (password == null) throw new OperationCanceledException();
                        input = new(path, password); info = await engine.InspectAsync(input, operation.Token);
                    }
                }
                else
                {
                    if (Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".bmp")) throw new PdfOperationException("Image", "Supported images: JPG, JPEG, PNG and BMP. WEBP is not enabled.");
                    if (new FileInfo(path).Length > PdfService.MaxInputBytes) throw new PdfOperationException("Limit", "The image exceeds 256 MiB.");
                }
                vm.Files.Add(new(input, info));
                vm.Recent.Remove(path); vm.Recent.Insert(0, path); if (vm.Recent.Count > 8) vm.Recent.RemoveAt(8);
                vm.Progress = 100d * (i + 1) / paths.Length;
            }
            vm.Status = $"{vm.Files.Count} files loaded. Sources are unchanged.";
        }
        catch (Exception exception) { Error(exception); }
        finally
        {
            vm.Busy = false; operation.Dispose(); operation = null;
            if (vm.Files.Count > 0) FilesList.SelectedIndex = 0;
            RefreshSelected();
        }
    }
    private string? AskPassword()
    {
        var dialog = new Window { Owner = this, Title = "Open encrypted PDF", Width = 450, Height = 260, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "Enter the valid PDF password. Passwords are used only in memory for this session and are never logged or written to disk." });
        var box = new PasswordBox { Margin = new(0, 16, 0, 16), Padding = new(8), FontSize = 16 }; panel.Children.Add(box);
        var ok = new Button { Content = "Open PDF", IsDefault = true }; ok.Click += (_, _) => dialog.DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true }; panel.Children.Add(ok); panel.Children.Add(cancel); dialog.Content = panel;
        dialog.Loaded += (_, _) => box.Focus();
        var result = dialog.ShowDialog() == true ? box.Password : null; box.Clear(); return result;
    }
    private async void RefreshSelected()
    {
        if (changing) return;
        changing = true;
        try
        {
            previewCancellation.Cancel(); previewCancellation.Dispose(); previewCancellation = new(); vm.Pages.Clear(); undo.Clear();
            var file = FilesList.SelectedItem as FileItem ?? vm.Files.FirstOrDefault();
            if (file == null) return;
            if (file.Info is { } info)
            {
                var index = vm.Files.IndexOf(file);
                for (var page = 1; page <= info.PageCount; page++) vm.Pages.Add(new(index, page, file.Name));
                TitleBox.Text = info.Metadata.Title; AuthorBox.Text = info.Metadata.Author; SubjectBox.Text = info.Metadata.Subject; KeywordsBox.Text = info.Metadata.Keywords;
                CreatorBox.Text = info.Metadata.Creator; ProducerBox.Text = info.Metadata.Producer;
            }
            else
            {
                try
                {
                    var token = previewCancellation.Token;
                    ImagePreview.Source = null;
                    changing = false;
                    var bitmap = await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream;
                        image.DecodePixelWidth = 480; image.CacheOption = BitmapCacheOption.OnLoad;
                        image.EndInit(); image.Freeze(); token.ThrowIfCancellationRequested(); return image;
                    }, token);
                    if (!token.IsCancellationRequested) ImagePreview.Source = bitmap;
                }
                catch (OperationCanceledException) { }
                catch (Exception exception) { ImagePreview.Source = null; Error(exception); }
            }
        }
        finally { changing = false; }
    }
    private void FileSelected(object sender, SelectionChangedEventArgs e) { if (!vm.Busy) RefreshSelected(); }
    private async void PageLoaded(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not PageItem page || page.Thumbnail != null || page.Source >= vm.Files.Count) return;
        var token = previewCancellation.Token; var input = vm.Files[page.Source].Input;
        try { page.Thumbnail = await thumbnails.GetAsync(input, page.Page, token); }
        catch (OperationCanceledException) { }
        catch { vm.Status = "Some page previews are unavailable. Export validation is independent of thumbnails."; }
    }
    private void Snapshot() { undo.Push(vm.Pages.Select(x => x.Selection).ToArray()); if (undo.Count > 50) { var keep = undo.Take(50).Reverse().ToArray(); undo.Clear(); foreach (var state in keep) undo.Push(state); } }
    private void Restore(IEnumerable<PageSelection> selections)
    {
        vm.Pages.Clear(); foreach (var x in selections) vm.Pages.Add(new(x.Source, x.Page, vm.Files[x.Source].Name, x.Rotation));
    }
    private void UndoClick(object sender, RoutedEventArgs e) { if (undo.TryPop(out var previous)) Restore(previous); }
    private PageItem[] SelectedPages() => PagesList.SelectedItems.Cast<PageItem>().ToArray();
    private void PageUp(object sender, RoutedEventArgs e) => MovePage(-1);
    private void PageDown(object sender, RoutedEventArgs e) => MovePage(1);
    private void MovePage(int delta)
    {
        if (PagesList.SelectedItem is not PageItem item) return; var index = vm.Pages.IndexOf(item); var target = index + delta;
        if (target < 0 || target >= vm.Pages.Count) return; Snapshot(); vm.Pages.Move(index, target); PagesList.SelectedItem = item;
    }
    private void RotateSelected(object sender, RoutedEventArgs e) { var selected = SelectedPages(); if (selected.Length == 0) return; Snapshot(); foreach (var page in selected) page.Rotation = (page.Rotation + 90) % 360; }
    private void DuplicateSelected(object sender, RoutedEventArgs e)
    {
        var selected = SelectedPages(); if (selected.Length == 0) return;
        if (vm.Pages.Count + selected.Length > PdfService.MaxOutputPages) { Error(new PdfOperationException("Limit", "Maximum 10,000 output pages.")); return; }
        Snapshot(); foreach (var page in selected.Reverse()) vm.Pages.Insert(vm.Pages.IndexOf(page) + 1, new(page.Source, page.Page, vm.Files[page.Source].Name, page.Rotation));
    }
    private void DeleteSelected(object sender, RoutedEventArgs e) { var selected = SelectedPages(); if (selected.Length == 0) return; Snapshot(); foreach (var page in selected) vm.Pages.Remove(page); }
    private void FileUp(object sender, RoutedEventArgs e) => MoveFile(-1);
    private void FileDown(object sender, RoutedEventArgs e) => MoveFile(1);
    private void MoveFile(int delta) { if (FilesList.SelectedItem is not FileItem file) return; var i = vm.Files.IndexOf(file); if (i + delta < 0 || i + delta >= vm.Files.Count) return; vm.Files.Move(i, i + delta); FilesList.SelectedItem = file; RefreshSelected(); }
    private void RemoveFile(object sender, RoutedEventArgs e) { if (FilesList.SelectedItem is FileItem file) vm.Files.Remove(file); FilesList.SelectedIndex = vm.Files.Count > 0 ? 0 : -1; RefreshSelected(); }
    private async void ClearFiles() { previewCancellation.Cancel(); vm.Files.Clear(); vm.Pages.Clear(); undo.Clear(); await thumbnails.ClearAsync(); ImagePreview.Source = null; }
    private void ClearWorkspace(object sender, RoutedEventArgs e) => ClearFiles();
    private void ClearMetadata(object sender, RoutedEventArgs e) { TitleBox.Clear(); AuthorBox.Clear(); SubjectBox.Clear(); KeywordsBox.Clear(); CreatorBox.Clear(); }
    private async void RecentOpen(object sender, MouseButtonEventArgs e) { if (RecentList.SelectedItem is string path) await OpenPaths([path]); }
    private static T? ContainerItem<T>(DependencyObject? element) where T : class
    {
        while (element != null) { if (element is ListBoxItem item) return item.DataContext as T; element = VisualTreeHelper.GetParent(element); } return null;
    }
    private void FileMouseDown(object sender, MouseButtonEventArgs e) { dragStart = e.GetPosition(this); draggedItem = ContainerItem<FileItem>(e.OriginalSource as DependencyObject); }
    private void PageMouseDown(object sender, MouseButtonEventArgs e) { dragStart = e.GetPosition(this); draggedItem = ContainerItem<PageItem>(e.OriginalSource as DependencyObject); }
    private void FileMouseMove(object sender, MouseEventArgs e) => DragItem(sender, e, "PDFToolbox.FileOrder", draggedItem as FileItem);
    private void PageMouseMove(object sender, MouseEventArgs e) { if (tool == "Organize") DragItem(sender, e, "PDFToolbox.PageOrder", draggedItem as PageItem); }
    private void DragItem(object sender, MouseEventArgs e, string format, object? item)
    {
        if (vm.Busy || e.LeftButton != MouseButtonState.Pressed || item == null || (e.GetPosition(this) - dragStart).Length < 8) return;
        draggedItem = null; DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(format, item), DragDropEffects.Move);
    }
    private void FileOrderDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("PDFToolbox.FileOrder")) return; e.Handled = true;
        if (vm.Busy || e.Data.GetData("PDFToolbox.FileOrder") is not FileItem file || ContainerItem<FileItem>(e.OriginalSource as DependencyObject) is not { } target) return;
        var from = vm.Files.IndexOf(file); var to = vm.Files.IndexOf(target); if (from < 0 || to < 0) return; vm.Files.Move(from, to); FilesList.SelectedItem = file; RefreshSelected();
    }
    private void PageOrderDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("PDFToolbox.PageOrder")) return; e.Handled = true;
        if (vm.Busy || tool != "Organize" || e.Data.GetData("PDFToolbox.PageOrder") is not PageItem page || ContainerItem<PageItem>(e.OriginalSource as DependencyObject) is not { } target) return;
        var from = vm.Pages.IndexOf(page); var to = vm.Pages.IndexOf(target); if (from < 0 || to < 0) return; Snapshot(); vm.Pages.Move(from, to); PagesList.SelectedItem = page;
    }
    private void FileDragOver(object sender, DragEventArgs e) { e.Effects = !vm.Busy && (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent("PDFToolbox.PageOrder") || e.Data.GetDataPresent("PDFToolbox.FileOrder")) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; }
    private async void FileDrop(object sender, DragEventArgs e) { if (vm.Busy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return; e.Handled = true; if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) await OpenPaths(paths); }

    private async void ExportClick(object sender, RoutedEventArgs e)
    {
        if (vm.Busy) return;
        ClearError();
        if (tool == "Home" || vm.Files.Count == 0) { Error(new PdfOperationException("Empty", "Choose a tool and open input files first.")); return; }
        var file = FilesList.SelectedItem as FileItem ?? vm.Files[0];
        var policy = (CollisionPolicy)new[] { 1, 0, 2 }[CollisionBox.SelectedIndex];
        var multiple = tool == "Split" || (tool == "Extract" && ExtractSeparate.IsChecked == true);
        string output;
        try
        {
            // Validate all user settings before opening output dialogs or producing any file.
            IReadOnlyList<PageSelection> picks = [];
            if (tool == "Organize") picks = vm.Pages.Select(x => x.Selection).ToArray();
            if (tool == "Merge") picks = vm.Files.SelectMany((f, i) => Enumerable.Range(1, f.Info!.PageCount).Select(p => new PageSelection(i, p))).ToArray();
            if (tool is "Extract" or "Rotate")
            {
                var selected = tool == "Rotate" && RotateAll.IsChecked == true ? Enumerable.Range(1, file.Info!.PageCount).ToArray() : PageRanges.Parse(RangesBox.Text, file.Info!.PageCount).ToArray();
                picks = tool == "Extract" ? selected.Select(p => new PageSelection(vm.Files.IndexOf(file), p)).ToArray() : Enumerable.Range(1, file.Info!.PageCount).Select(p => new PageSelection(vm.Files.IndexOf(file), p, selected.Contains(p) ? 90 * (RotationBox.SelectedIndex + 1) : 0)).ToArray();
            }
            if (tool is not ("Split" or "Metadata" or "Images") && picks.Count == 0) throw new PdfOperationException("Empty", "The export plan has no pages.");
            var n = 1;
            if (tool == "Split")
            {
                if (!int.TryParse(EveryNBox.Text, NumberStyles.None, CultureInfo.InvariantCulture, out n)) throw new PdfOperationException("Range", "Pages per part must be a positive integer.");
                PageRanges.Split((SplitMode)SplitModeBox.SelectedIndex, RangesBox.Text, file.Info!.PageCount, n);
            }
            var imageOptions = new ImageOptions();
            if (tool == "Images")
            {
                if (!double.TryParse(MarginsBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var margin) || !int.TryParse(QualityBox.Text, out var quality)) throw new PdfOperationException("Image", "Enter a numeric margin and JPEG quality.");
                imageOptions = new(PageSizeBox.SelectedIndex == 0 ? "A4" : "Letter", LandscapeBox.IsChecked == true, margin, (ImageFit)FitBox.SelectedIndex, quality);
            }
            var metadata = new PdfMetadata(TitleBox.Text, AuthorBox.Text, SubjectBox.Text, KeywordsBox.Text, CreatorBox.Text, ProducerBox.Text);
            if (multiple)
            {
                if (policy == CollisionPolicy.Replace) throw new PdfOperationException("Output", "For multiple outputs, use Rename automatically or Cancel; replacement is not supported.");
                var folder = new OpenFolderDialog { Title = "Choose output folder", Multiselect = false }; if (folder.ShowDialog(this) != true) return; output = folder.FolderName;
            }
            else
            {
                var save = new SaveFileDialog { Filter = "PDF document (*.pdf)|*.pdf", DefaultExt = ".pdf", AddExtension = true, OverwritePrompt = false, FileName = Path.GetFileNameWithoutExtension(file.Path) + "-" + tool.ToLowerInvariant() + ".pdf" };
                if (save.ShowDialog(this) != true) return; output = save.FileName;
                if (policy == CollisionPolicy.Replace)
                {
                    if (!File.Exists(output)) policy = CollisionPolicy.Cancel;
                    else if (MessageBox.Show(this, "Replace the existing OUTPUT file? Source files are always protected.\n\n" + output, "Confirm output replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                }
            }
            vm.Busy = true; operation = new(); vm.Progress = 0; vm.Status = "Preparing export…"; vm.Results.Clear(); ResultsPanel.Visibility = Visibility.Visible;
            var progress = new Progress<JobProgress>(p => { vm.Progress = p.Total > 0 ? 100d * p.Completed / p.Total : 0; vm.Status = p.Stage; });
            if (multiple)
            {
                var mode = tool == "Split" ? (SplitMode)SplitModeBox.SelectedIndex : SplitMode.SelectedPages;
                var name = tool == "Split" ? SplitNameBox.Text : Path.GetFileNameWithoutExtension(file.Path) + "-extract";
                var results = await engine.SplitAsync(file.Input, mode, RangesBox.Text, n, output, name, policy, progress, operation.Token);
                foreach (var result in results) vm.Results.Add(result);
            }
            else
            {
                OutputResult result;
                if (tool == "Metadata") result = await engine.MetadataAsync(file.Input, metadata, output, policy, operation.Token);
                else if (tool == "Images") result = await engine.ImagesAsync(vm.Files.Select(f => f.Path).ToArray(), imageOptions, output, policy, progress, operation.Token);
                else result = await engine.ExportPagesAsync(vm.Files.Select(f => f.Input).ToArray(), picks, output, policy, progress, operation.Token);
                vm.Results.Add(result);
            }
            var successes = vm.Results.Count(x => x.Status == "Success");
            vm.Status = $"Export finished: {successes} succeeded, {vm.Results.Count - successes} failed/skipped. Sources unchanged.";
        }
        catch (Exception exception) { Error(exception); }
        finally { vm.Busy = false; operation?.Dispose(); operation = null; }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void ThemeClick(object sender, RoutedEventArgs e)
    {
        light = !light;
        var values = light ? new[] { "#F3F6FA", "#FFFFFF", "#E8EFF6", "#182A40", "#52657D", "#C9D7E5", "#77CCC1", "#8E2A16" } : new[] { "#0D141F", "#152130", "#1C2C3E", "#EDF4FC", "#A4B7CE", "#2B3F55", "#87D8D0", "#FFBCAC" };
        var keys = new[] { "Background", "Surface", "SurfaceAlt", "Text", "Muted", "Line", "Accent", "Error" };
        for (var i = 0; i < keys.Length; i++) Application.Current.Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(values[i]));
    }
    private void WindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (vm.Busy) { operation?.Cancel(); e.Cancel = true; vm.Status = "Cancellation requested. Wait for the current parsing/save step, then close."; return; }
        previewCancellation.Cancel(); vm.Files.Clear(); vm.Pages.Clear(); vm.Recent.Clear();
    }
}
