using System.Runtime.InteropServices;

namespace GamingHub.Core.Services;

/// <summary>Safely deletes files, optionally sending them to the recycle bin.</summary>
public sealed class FileDeletionService
{
    public sealed record DeleteResult(int Deleted, int Failed, long BytesFreed, List<string> Errors);

    public DeleteResult Delete(IEnumerable<string> paths, bool useRecycleBin)
    {
        int deleted = 0, failed = 0;
        long bytes = 0;
        var errors = new List<string>();

        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                long size = 0;
                try { size = new FileInfo(path).Length; } catch { }

                if (useRecycleBin)
                    ToRecycleBin(path);
                else
                    File.Delete(path);

                deleted++;
                bytes += size;
            }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{path}: {ex.Message}");
            }
        }

        return new DeleteResult(deleted, failed, bytes, errors);
    }

    private static void ToRecycleBin(string path)
    {
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"Recycle bin operation failed (code {result}).");
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
