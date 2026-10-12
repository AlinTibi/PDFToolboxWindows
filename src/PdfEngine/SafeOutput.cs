using System.Runtime.InteropServices;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace Almarfeld.PdfEngine;

public static class SafeOutput
{
    public static string Commit(string target, CollisionPolicy policy, IEnumerable<string> sources, Action<Stream> write, Action<string> validate, CancellationToken cancellation)
    {
        target = System.IO.Path.GetFullPath(target);
        var inputPaths = sources.Select(System.IO.Path.GetFullPath).ToArray();
        Guard(target, inputPaths);
        var directory = System.IO.Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory)) throw new PdfOperationException("Output", "Choose an existing output folder.");
        var temporary = System.IO.Path.Combine(directory, $".pdftoolbox-{Guid.NewGuid():N}.tmp");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                write(stream);
                stream.Flush(true);
            }
            cancellation.ThrowIfCancellationRequested();
            validate(temporary);
            cancellation.ThrowIfCancellationRequested();
            for (var suffix = 0; suffix < 10000; suffix++)
            {
                var candidate = suffix == 0 ? target : System.IO.Path.Combine(directory, $"{System.IO.Path.GetFileNameWithoutExtension(target)} ({suffix + 1}){System.IO.Path.GetExtension(target)}");
                Guard(candidate, inputPaths);
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    if (policy == CollisionPolicy.Replace && File.Exists(candidate)) File.Replace(temporary, candidate, null);
                    else File.Move(temporary, candidate, false);
                    return candidate;
                }
                catch (IOException) when (policy == CollisionPolicy.Rename && File.Exists(candidate)) { }
            }
            throw new PdfOperationException("Output", "No unused output filename was available.");
        }
        finally
        {
            // Only the GUID-named temporary created by this operation can be removed.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void Guard(string target, string[] sources)
    {
        if (sources.Any(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)))
            throw new PdfOperationException("Source", "Export to a different file. This milestone never overwrites a source file.");
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new PdfOperationException("Output", "Replacing a linked output file is not supported.");
            using var output = File.OpenHandle(target, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!GetFileInformationByHandle(output, out var outputId)) throw new IOException("Cannot verify output file identity.");
            foreach (var source in sources)
            {
                using var input = File.OpenHandle(source, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!GetFileInformationByHandle(input, out var id)) throw new IOException("Cannot verify source file identity.");
                if (id.Volume == outputId.Volume && id.High == outputId.High && id.Low == outputId.Low)
                    throw new PdfOperationException("Source", "The output aliases a source file. Choose a different filename.");
            }
        }
        // Avoid ambiguous redirection through an output-folder junction/symlink.
        for (var d = new DirectoryInfo(System.IO.Path.GetDirectoryName(target)!); d != null; d = d.Parent)
            if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new PdfOperationException("Output", "Choose an output folder without junctions or symbolic links.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdentity
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint Volume, SizeHigh, SizeLow, Links, High, Low;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileIdentity info);
}
