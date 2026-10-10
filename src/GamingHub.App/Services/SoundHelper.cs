using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace GamingHub.App.Services;

/// <summary>Plays a short, quiet intro sound on startup via the Windows wave API.</summary>
public static class SoundHelper
{
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    private static string? _cachedPath;

    public static void PlayIntro()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            string? path = EnsureWaveFile();
            if (path is not null)
                PlaySound(path, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
        }
        catch
        {
            // sound is optional
        }
    }

    private static string? EnsureWaveFile()
    {
        if (_cachedPath is not null && File.Exists(_cachedPath))
            return _cachedPath;

        var uri = new Uri("avares://GamingHub.App/Assets/intro.wav");
        if (!AssetLoader.Exists(uri))
            return null;

        using var src = AssetLoader.Open(uri);
        string temp = Path.Combine(Path.GetTempPath(), "gaminghub_intro.wav");
        using (var fs = File.Create(temp))
            src.CopyTo(fs);

        _cachedPath = temp;
        return temp;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);
}
