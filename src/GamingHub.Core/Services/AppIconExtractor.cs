using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GamingHub.Core.Services;

/// <summary>
/// Extrahiert möglichst hochauflösende Anwendungs-Icons (bis 256×256) aus einer
/// EXE. Zuerst über die Windows-System-ImageList („Jumbo“-Icons), danach als
/// Rückfall das zugeordnete Icon. Ohne diesen Weg liefert
/// <see cref="Icon.ExtractAssociatedIcon(string)"/> nur 32×32-Pixel – viel zu
/// unscharf für die Spielkarten.
/// </summary>
internal static class AppIconExtractor
{
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;

    private const uint SHGFI_SYSICONINDEX = 0x000004000;
    private const int ILD_TRANSPARENT = 0x1;

    // Reihenfolge des Windows-ImageLists: von groß nach klein versuchen.
    private static readonly int[] ImageListSizes = { SHIL_JUMBO, SHIL_EXTRALARGE, SHIL_LARGE };

    /// <summary>Schreibt das größte verfügbare Icon der Datei als PNG.
    /// Gibt true zurück, wenn eine Datei erzeugt wurde.</summary>
    public static bool TryExtract(string exePath, string outputPng)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return false;

        foreach (int size in ImageListSizes)
        {
            try
            {
                using var bitmap = GetShellIcon(exePath, size);
                if (bitmap is not null && bitmap.Width >= 48)
                {
                    bitmap.Save(outputPng, ImageFormat.Png);
                    return true;
                }
            }
            catch
            {
                // nächste Größe versuchen
            }
        }

        // Rückfall: zugeordnetes Icon (meist 32×32).
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is not null)
            {
                using var bitmap = icon.ToBitmap();
                bitmap.Save(outputPng, ImageFormat.Png);
                return true;
            }
        }
        catch
        {
            // kein Icon extrahierbar
        }

        return false;
    }

    /// <summary>Prüft, ob eine bereits gecachte Datei hochauflösend ist
    /// (damit alte 32×32-Icons einmalig neu erzeugt werden).</summary>
    public static bool IsHighResolution(string pngPath)
    {
        try
        {
            if (!File.Exists(pngPath)) return false;
            using var bitmap = new Bitmap(pngPath);
            return bitmap.Width >= 48;
        }
        catch
        {
            return false;
        }
    }

    private static Bitmap? GetShellIcon(string path, int imageListSize)
    {
        var info = new SHFILEINFO();
        IntPtr result = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(info), SHGFI_SYSICONINDEX);
        if (result == IntPtr.Zero || info.iIcon < 0)
            return null;

        Guid iidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");
        int hr = SHGetImageList(imageListSize, ref iidImageList, out IImageList? list);
        if (hr != 0 || list is null)
            return null;

        IntPtr hIcon = IntPtr.Zero;
        try
        {
            list.GetIcon(info.iIcon, ILD_TRANSPARENT, ref hIcon);
            if (hIcon == IntPtr.Zero)
                return null;

            using var icon = Icon.FromHandle(hIcon);
            return icon.ToBitmap();
        }
        finally
        {
            list.ReleaseComObjectSafe();
            if (hIcon != IntPtr.Zero)
                DestroyIcon(hIcon);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList? ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }
}

internal static class ComReleaseExtensions
{
    public static void ReleaseComObjectSafe(this object? comObject)
    {
        if (comObject is null) return;
        try
        {
            if (Marshal.IsComObject(comObject))
                Marshal.ReleaseComObject(comObject);
        }
        catch
        {
            // ignorieren – Freigabe ist nur Best-Effort
        }
    }
}
