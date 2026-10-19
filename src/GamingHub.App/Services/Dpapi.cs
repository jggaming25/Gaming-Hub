using System.Runtime.InteropServices;

namespace GamingHub.App.Services;

/// <summary>
/// Windows-DPAPI (CryptProtectData) zum Verschlüsseln lokaler Geheimnisse wie
/// der Login-Session. Nur das angemeldete Windows-Konto kann die Daten lesen.
/// </summary>
internal static class Dpapi
{
    private const int UiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        IntPtr optionalEntropy,
        IntPtr reserved,
        IntPtr prompt,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    /// <summary>Verschlüsselt Daten für das aktuelle Windows-Benutzerkonto.
    /// Gibt null zurück, wenn DPAPI nicht verfügbar ist.</summary>
    public static byte[]? Protect(byte[] data)
    {
        if (data.Length == 0) return null;

        var input = new DataBlob();
        var output = new DataBlob();
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            input.Size = data.Length;
            input.Data = handle.AddrOfPinnedObject();

            if (!CryptProtectData(ref input, "GamingHub session", IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out output))
                return null;

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            handle.Free();
        }
    }

    /// <summary>Entschlüsselt DPAPI-Daten. Gibt null zurück, wenn die Daten
    /// nicht zum aktuellen Benutzer passen oder beschädigt sind.</summary>
    public static byte[]? Unprotect(byte[] data)
    {
        if (data.Length == 0) return null;

        var input = new DataBlob();
        var output = new DataBlob();
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            input.Size = data.Length;
            input.Data = handle.AddrOfPinnedObject();

            if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, UiForbidden, out output))
                return null;

            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (output.Data != IntPtr.Zero) LocalFree(output.Data);
            handle.Free();
        }
    }
}
