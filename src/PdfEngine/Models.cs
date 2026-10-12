namespace Almarfeld.PdfEngine;

public sealed class PdfInput(string path, string? password = null)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    internal string? Password { get; } = password;
    internal Guid SessionId { get; } = Guid.NewGuid();
    public override string ToString() => System.IO.Path.GetFileName(Path);
}
public sealed record PdfMetadata(string Title = "", string Author = "", string Subject = "", string Keywords = "", string Creator = "", string Producer = "");
public sealed record PdfInfo(string Path, int PageCount, long Bytes, bool Encrypted, bool Signed, bool Forms, PdfMetadata Metadata, IReadOnlyList<int> Rotations);
public sealed record PageSelection(int Source, int Page, int Rotation = 0);
public enum CollisionPolicy { Cancel, Rename, Replace }
public enum SplitMode { Ranges, EveryPage, EveryN, SelectedPages }
public enum ImageFit { Fit, Fill }
public enum RasterFormat { Png, Jpeg }
public enum OverlayPosition { Center, TopLeft, TopCenter, TopRight, CenterLeft, CenterRight, BottomLeft, BottomCenter, BottomRight }
public enum NumberFormat { Number, PageNumber, NumberOfTotal, PageNumberOfTotal }
public sealed record RasterOptions(RasterFormat Format = RasterFormat.Png, int Dpi = 144, int Quality = 90, string Prefix = "document");
public sealed record WatermarkOptions(string Text = "DRAFT", string? ImagePath = null, double FontSize = 48, double Opacity = .3,
    double Rotation = -30, OverlayPosition Position = OverlayPosition.Center, double OffsetX = 0, double OffsetY = 0, double Scale = .3);
public sealed record NumberOptions(int Start = 1, string Prefix = "", string Suffix = "", NumberFormat Format = NumberFormat.PageNumberOfTotal,
    double FontSize = 12, double Margin = 24, OverlayPosition Position = OverlayPosition.BottomCenter, int Skip = 0);
public sealed record ImageOptions(string PageSize = "A4", bool Landscape = false, double Margin = 24, ImageFit Fit = ImageFit.Fit, int Quality = 90);
public sealed record JobProgress(int Completed, int Total, string Stage);
public sealed record OutputResult(string Path, string Status, string Message, long Bytes = 0);
public sealed class PdfOperationException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}
