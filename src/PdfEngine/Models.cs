namespace Almarfeld.PdfEngine;

public sealed class PdfInput(string path, string? password = null)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    internal string? Password { get; } = password;
    public override string ToString() => System.IO.Path.GetFileName(Path);
}
public sealed record PdfMetadata(string Title = "", string Author = "", string Subject = "", string Keywords = "", string Creator = "", string Producer = "");
public sealed record PdfInfo(string Path, int PageCount, long Bytes, bool Encrypted, bool Signed, bool Forms, PdfMetadata Metadata, IReadOnlyList<int> Rotations);
public sealed record PageSelection(int Source, int Page, int Rotation = 0);
public enum CollisionPolicy { Cancel, Rename, Replace }
public enum SplitMode { Ranges, EveryPage, EveryN, SelectedPages }
public enum ImageFit { Fit, Fill }
public sealed record ImageOptions(string PageSize = "A4", bool Landscape = false, double Margin = 24, ImageFit Fit = ImageFit.Fit, int Quality = 90);
public sealed record JobProgress(int Completed, int Total, string Stage);
public sealed record OutputResult(string Path, string Status, string Message, long Bytes = 0);
public sealed class PdfOperationException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
