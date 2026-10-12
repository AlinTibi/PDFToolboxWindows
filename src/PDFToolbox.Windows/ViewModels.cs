using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using Almarfeld.PdfEngine;

namespace Almarfeld.PdfToolbox;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class FileItem(PdfInput input, PdfInfo? info) : Observable
{
    internal PdfInput Input { get; } = input;
    public PdfInfo? Info { get; } = info;
    public string Path => Input.Path;
    public string Name => System.IO.Path.GetFileName(Path);
    public override string ToString() => Name;
    public string Summary => Info == null ? "Image · source unchanged" : $"{Info.PageCount} pages · {Info.Bytes / 1024d / 1024:F1} MiB{(Info.Encrypted ? " · encrypted (valid credentials required)" : "")}{(Info.Signed ? " · signed (editing blocked)" : "")}{(Info.Forms ? " · forms (page editing blocked)" : "")}";
}
public sealed class PageItem(int source, int page, string file, int rotation = 0) : Observable
{
    public Guid Id { get; } = Guid.NewGuid();
    public int Source { get; } = source;
    public int Page { get; } = page;
    public string Label => $"Page {Page} · {file}";
    public override string ToString() => Label;
    private string previewNote = "Preview loads when visible";
    public string PreviewNote { get => previewNote; set { previewNote = value; Changed(); } }
    private int rotation = rotation;
    public int Rotation { get => rotation; set { rotation = value; Changed(); } }
    private BitmapSource? thumbnail;
    public BitmapSource? Thumbnail { get => thumbnail; set { thumbnail = value; Changed(); } }
    public PageSelection Selection => new(Source, Page, Rotation);
}
public sealed class Workspace : Observable
{
    public ObservableCollection<FileItem> Files { get; } = [];
    public ObservableCollection<PageItem> Pages { get; } = [];
    public ObservableCollection<string> Recent { get; } = [];
    public ObservableCollection<OutputResult> Results { get; } = [];
    private bool busy;
    public bool Busy { get => busy; set { busy = value; Changed(); Changed(nameof(Idle)); } }
    public bool Idle => !Busy;
    private string status = "Open files or choose a tool to get started.";
    public string Status { get => status; set { status = value; Changed(); } }
    private double progress;
    public double Progress { get => progress; set { progress = value; Changed(); } }
}
