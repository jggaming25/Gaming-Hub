using System.Net.Sockets;
using System.Text;

namespace GamingHub.Core.Services;

/// <summary>
/// Minimaler Client für das binäre OpenRGB-SDK-Protokoll (TCP, Standardport 6742).
/// Verhandelt bewusst Protokollversion 3 (OpenRGB 0.7+): neuere Server handeln
/// automatisch auf die kleinere Version herunter, das Datenformat bleibt dabei
/// einfach und stabil. Ist das Datenformat an der OpenRGB-C++-Referenz
/// (RGBController::Set/GetDeviceDescription) ausgerichtet.
/// </summary>
internal sealed class OpenRgbClient : IDisposable
{
    /// <summary>Höchste vom Client unterstützte Protokollversion.</summary>
    public const int ClientProtocolVersion = 3;

    private const string Magic = "ORGB";

    private static class Packet
    {
        public const uint RequestControllerCount = 0;
        public const uint RequestControllerData = 1;
        public const uint Ack = 10;
        public const uint RequestProtocolVersion = 40;
        public const uint SetClientName = 50;
        public const uint DeviceListUpdated = 100;
        public const uint UpdateLeds = 1050;
        public const uint UpdateZoneLeds = 1051;
        public const uint SetCustomMode = 1100;
        public const uint UpdateMode = 1101;
    }

    private readonly object _gate = new();
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public int ProtocolVersion { get; private set; }
    public bool DeviceListDirty { get; private set; }
    public bool IsConnected => _stream != null && _tcp is { Connected: true };

    // ---------------------------------------------------------------
    // Verbindung
    // ---------------------------------------------------------------

    public void Connect(string host, int port, string clientName)
    {
        Disconnect();
        lock (_gate)
        {
            var tcp = new TcpClient { NoDelay = true };
            try
            {
                if (!tcp.ConnectAsync(host, port).Wait(5000))
                    throw new IOException($"OpenRGB unter {host}:{port} antwortet nicht.");
            }
            catch (AggregateException ex) when (ex.InnerException is SocketException se)
            {
                tcp.Dispose();
                throw new IOException($"OpenRGB unter {host}:{port} nicht erreichbar: {se.Message}");
            }

            _tcp = tcp;
            _stream = tcp.GetStream();
            _stream.ReadTimeout = 4000;
            _stream.WriteTimeout = 4000;

            Send(Packet.SetClientName, 0, Encoding.UTF8.GetBytes(clientName + "\0"));
            ProtocolVersion = NegotiateProtocol();
        }
    }

    public void Disconnect()
    {
        lock (_gate)
        {
            try { _stream?.Dispose(); } catch { }
            try { _tcp?.Dispose(); } catch { }
            _stream = null;
            _tcp = null;
            ProtocolVersion = 0;
        }
    }

    public void Dispose() => Disconnect();

    private int NegotiateProtocol()
    {
        try
        {
            _stream!.ReadTimeout = 1200;
            Send(Packet.RequestProtocolVersion, 0, BitConverter.GetBytes((uint)ClientProtocolVersion));
            if (TryReceive(out var pkt) && pkt.Id == Packet.RequestProtocolVersion && pkt.Data.Length >= 4)
            {
                var server = BitConverter.ToInt32(pkt.Data, 0);
                return Math.Clamp(server, 0, ClientProtocolVersion);
            }
        }
        catch
        {
            // Kein Protokoll-Handshake -> sehr alte Version 0
        }
        finally
        {
            if (_stream != null) _stream.ReadTimeout = 4000;
        }
        return 0;
    }

    // ---------------------------------------------------------------
    // Geräte
    // ---------------------------------------------------------------

    public List<OpenRgbController> GetControllers()
    {
        lock (_gate)
        {
            EnsureConnected();
            DeviceListDirty = false;

            Send(Packet.RequestControllerCount, 0, Array.Empty<byte>());
            var countPkt = ReceiveMatching(Packet.RequestControllerCount, 0);
            var count = countPkt.Data.Length >= 4 ? BitConverter.ToUInt32(countPkt.Data, 0) : 0u;

            var list = new List<OpenRgbController>();
            for (uint i = 0; i < count && i < 512; i++)
            {
                try
                {
                    var controller = RequestControllerData(i);
                    if (controller != null)
                    {
                        controller.Index = i;
                        list.Add(controller);
                    }
                }
                catch
                {
                    // Ein fehlerhaftes Gerät darf die restlichen nicht verhindern.
                }
            }
            return list;
        }
    }

    private OpenRgbController? RequestControllerData(uint index)
    {
        var request = ProtocolVersion >= 1
            ? BitConverter.GetBytes(ProtocolVersion)
            : Array.Empty<byte>();
        Send(Packet.RequestControllerData, index, request);
        var pkt = ReceiveMatching(Packet.RequestControllerData, index);
        return ParseDevice(pkt.Data, ProtocolVersion);
    }

    // ---------------------------------------------------------------
    // Steuerung
    // ---------------------------------------------------------------

    /// <summary>Controller in den Custom/Direct-Modus schalten (freie Farbwahl).</summary>
    public void SetCustomMode(uint deviceIndex)
    {
        lock (_gate)
        {
            EnsureConnected();
            Send(Packet.SetCustomMode, deviceIndex, Array.Empty<byte>());
        }
    }

    /// <summary>Farbe auf alle LEDs eines Controllers setzen (Custom/Direct-Modus).</summary>
    public void SetColor(uint deviceIndex, int ledCount, RgbColor color)
    {
        if (ledCount <= 0) return;
        lock (_gate)
        {
            EnsureConnected();
            Send(Packet.SetCustomMode, deviceIndex, Array.Empty<byte>());

            var payload = new byte[2 + ledCount * 4];
            BitConverter.GetBytes((ushort)Math.Min(ledCount, ushort.MaxValue)).CopyTo(payload, 0);
            for (var i = 0; i < ledCount; i++)
            {
                var o = 2 + i * 4;
                payload[o] = color.R;
                payload[o + 1] = color.G;
                payload[o + 2] = color.B;
                payload[o + 3] = 0;
            }
            Send(Packet.UpdateLeds, deviceIndex, BuildBlock(payload));
        }
    }

    /// <summary>Farbe auf die LEDs einer Zone setzen.</summary>
    public void SetZoneColor(uint deviceIndex, uint zoneIndex, int ledCount, RgbColor color)
    {
        if (ledCount <= 0) return;
        lock (_gate)
        {
            EnsureConnected();
            Send(Packet.SetCustomMode, deviceIndex, Array.Empty<byte>());

            var payload = new byte[4 + 2 + ledCount * 4];
            BitConverter.GetBytes(zoneIndex).CopyTo(payload, 0);
            BitConverter.GetBytes((ushort)Math.Min(ledCount, ushort.MaxValue)).CopyTo(payload, 4);
            for (var i = 0; i < ledCount; i++)
            {
                var o = 6 + i * 4;
                payload[o] = color.R;
                payload[o + 1] = color.G;
                payload[o + 2] = color.B;
                payload[o + 3] = 0;
            }
            Send(Packet.UpdateZoneLeds, deviceIndex, BuildBlock(payload));
        }
    }

    /// <summary>Aktiven Modus (Effekt) eines Controllers setzen.</summary>
    public void SetMode(uint deviceIndex, OpenRgbMode mode)
    {
        lock (_gate)
        {
            EnsureConnected();

            var modeData = new List<byte>();
            WriteMode(modeData, mode, ProtocolVersion);

            var payload = new List<byte>();
            WriteI32(payload, mode.Index);
            payload.AddRange(modeData);

            Send(Packet.UpdateMode, deviceIndex, BuildBlock(payload.ToArray()));
        }
    }

    // ---------------------------------------------------------------
    // Paket-I/O
    // ---------------------------------------------------------------

    private void EnsureConnected()
    {
        if (!IsConnected)
            throw new IOException("Nicht mit OpenRGB verbunden.");
    }

    private void Send(uint id, uint deviceIndex, byte[] data)
    {
        if (_stream == null) throw new IOException("Nicht mit OpenRGB verbunden.");

        var header = new byte[16];
        Encoding.ASCII.GetBytes(Magic, 0, 4, header, 0);
        BitConverter.GetBytes(deviceIndex).CopyTo(header, 4);
        BitConverter.GetBytes(id).CopyTo(header, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(header, 12);

        _stream.Write(header, 0, header.Length);
        if (data.Length > 0) _stream.Write(data, 0, data.Length);
        _stream.Flush();
    }

    private static byte[] BuildBlock(byte[] payload)
    {
        // OpenRGB erwartet als erstes Feld die Gesamtgröße des Datenblocks
        // (inkl. dieses Größenfeldes selbst).
        var block = new byte[4 + payload.Length];
        BitConverter.GetBytes((uint)block.Length).CopyTo(block, 0);
        payload.CopyTo(block, 4);
        return block;
    }

    private IncomingPacket ReceiveMatching(uint id, uint deviceIndex)
    {
        for (var attempt = 0; attempt < 256; attempt++)
        {
            var pkt = Receive();
            if (pkt.Id == Packet.DeviceListUpdated) { DeviceListDirty = true; continue; }
            if (pkt.Id == Packet.Ack) continue;
            if (pkt.Id == id && (id == Packet.RequestControllerCount || pkt.DeviceIndex == deviceIndex))
                return pkt;
        }
        throw new IOException("Unerwartete Antwort von OpenRGB.");
    }

    private bool TryReceive(out IncomingPacket packet)
    {
        try { packet = Receive(); return true; }
        catch { packet = default; return false; }
    }

    private IncomingPacket Receive()
    {
        var header = ReadExact(16);
        if (Encoding.ASCII.GetString(header, 0, 4) != Magic)
            throw new IOException("Ungültiges OpenRGB-Paket empfangen.");

        var deviceIndex = BitConverter.ToUInt32(header, 4);
        var id = BitConverter.ToUInt32(header, 8);
        var size = BitConverter.ToInt32(header, 12);
        if (size < 0 || size > 64 * 1024 * 1024)
            throw new IOException("Ungültige OpenRGB-Paketgröße.");

        var data = size > 0 ? ReadExact(size) : Array.Empty<byte>();
        return new IncomingPacket(deviceIndex, id, data);
    }

    private byte[] ReadExact(int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = _stream!.Read(buffer, offset, count - offset);
            if (read <= 0) throw new IOException("Verbindung zu OpenRGB wurde geschlossen.");
            offset += read;
        }
        return buffer;
    }

    // ---------------------------------------------------------------
    // Gerätebeschreibung parsen (Protokoll <= 3)
    // ---------------------------------------------------------------

    private static OpenRgbController ParseDevice(byte[] data, int pv)
    {
        var pos = 4; // führendes data_size-Feld überspringen
        var controller = new OpenRgbController
        {
            Type = ReadI32(data, ref pos),
            Name = ReadString(data, ref pos)
        };

        if (pv >= 1) controller.Vendor = ReadString(data, ref pos);
        ReadString(data, ref pos); // description
        ReadString(data, ref pos); // version
        ReadString(data, ref pos); // serial
        ReadString(data, ref pos); // location

        var modeCount = ReadU16(data, ref pos);
        controller.ActiveMode = ReadI32(data, ref pos);
        for (var i = 0; i < modeCount; i++)
            controller.Modes.Add(ReadMode(data, ref pos, pv, i));

        var zoneCount = ReadU16(data, ref pos);
        for (var i = 0; i < zoneCount; i++)
            controller.Zones.Add(ReadZone(data, ref pos, pv));

        var ledCount = ReadU16(data, ref pos);
        controller.LedCount = ledCount;
        for (var i = 0; i < ledCount; i++)
        {
            ReadString(data, ref pos);       // LED-Name
            if (pv < 6) ReadI32(data, ref pos); // LED-Value
        }

        var colorCount = ReadU16(data, ref pos);
        for (var i = 0; i < colorCount; i++)
            controller.Colors.Add(ReadColor(data, ref pos));

        return controller;
    }

    private static OpenRgbMode ReadMode(byte[] data, ref int pos, int pv, int index)
    {
        var mode = new OpenRgbMode
        {
            Index = index,
            Name = ReadString(data, ref pos)
        };

        if (pv < 6) mode.Value = ReadI32(data, ref pos);
        mode.Flags = ReadU32(data, ref pos);
        mode.SpeedMin = ReadU32(data, ref pos);
        mode.SpeedMax = ReadU32(data, ref pos);
        if (pv >= 3) { mode.BrightnessMin = ReadU32(data, ref pos); mode.BrightnessMax = ReadU32(data, ref pos); }
        mode.ColorsMin = ReadU32(data, ref pos);
        mode.ColorsMax = ReadU32(data, ref pos);
        mode.Speed = ReadU32(data, ref pos);
        if (pv >= 3) mode.Brightness = ReadU32(data, ref pos);
        mode.Direction = ReadU32(data, ref pos);
        mode.ColorMode = ReadU32(data, ref pos);

        var colorCount = ReadU16(data, ref pos);
        for (var i = 0; i < colorCount; i++)
            mode.Colors.Add(ReadColor(data, ref pos));

        return mode;
    }

    private static OpenRgbZone ReadZone(byte[] data, ref int pos, int pv)
    {
        var zone = new OpenRgbZone { Name = ReadString(data, ref pos) };
        zone.Type = ReadI32(data, ref pos);
        ReadU32(data, ref pos); // leds_min
        ReadU32(data, ref pos); // leds_max
        zone.LedCount = (int)ReadU32(data, ref pos);

        var matrixLen = ReadU16(data, ref pos);
        pos += matrixLen;

        if (pv >= 4)
        {
            var segments = ReadU16(data, ref pos);
            for (var i = 0; i < segments; i++)
            {
                ReadString(data, ref pos);
                ReadI32(data, ref pos);
                ReadU32(data, ref pos);
                ReadU32(data, ref pos);
                if (pv >= 6) { var ml = ReadU16(data, ref pos); pos += ml; ReadU32(data, ref pos); }
            }
        }

        if (pv >= 5) ReadU32(data, ref pos); // zone flags
        if (pv >= 6)
        {
            ReadI32(data, ref pos);
            var zoneModes = ReadU16(data, ref pos);
            for (var i = 0; i < zoneModes; i++) ReadMode(data, ref pos, pv, i);
            ReadString(data, ref pos);
        }

        return zone;
    }

    private static void WriteMode(List<byte> buffer, OpenRgbMode mode, int pv)
    {
        WriteString(buffer, mode.Name);
        if (pv < 6) WriteI32(buffer, mode.Value);
        WriteU32(buffer, mode.Flags);
        WriteU32(buffer, mode.SpeedMin);
        WriteU32(buffer, mode.SpeedMax);
        if (pv >= 3) { WriteU32(buffer, mode.BrightnessMin); WriteU32(buffer, mode.BrightnessMax); }
        WriteU32(buffer, mode.ColorsMin);
        WriteU32(buffer, mode.ColorsMax);
        WriteU32(buffer, mode.Speed);
        if (pv >= 3) WriteU32(buffer, mode.Brightness);
        WriteU32(buffer, mode.Direction);
        WriteU32(buffer, mode.ColorMode);
        WriteU16(buffer, (ushort)Math.Min(mode.Colors.Count, ushort.MaxValue));
        foreach (var c in mode.Colors)
        {
            buffer.Add(c.R);
            buffer.Add(c.G);
            buffer.Add(c.B);
            buffer.Add(0);
        }
    }

    // ---------------------------------------------------------------
    // Primitive Helfer
    // ---------------------------------------------------------------

    private static string ReadString(byte[] data, ref int pos)
    {
        if (pos + 2 > data.Length) { pos = data.Length; return string.Empty; }
        var len = BitConverter.ToUInt16(data, pos);
        pos += 2;
        if (len == 0) return string.Empty;
        if (pos + len > data.Length) len = (ushort)Math.Max(0, data.Length - pos);
        var value = Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return value.TrimEnd('\0');
    }

    private static RgbColor ReadColor(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) { pos = data.Length; return new RgbColor(0, 0, 0); }
        var color = new RgbColor(data[pos], data[pos + 1], data[pos + 2]);
        pos += 4;
        return color;
    }

    private static int ReadI32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) { pos = data.Length; return 0; }
        var value = BitConverter.ToInt32(data, pos);
        pos += 4;
        return value;
    }

    private static uint ReadU32(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length) { pos = data.Length; return 0; }
        var value = BitConverter.ToUInt32(data, pos);
        pos += 4;
        return value;
    }

    private static int ReadU16(byte[] data, ref int pos)
    {
        if (pos + 2 > data.Length) { pos = data.Length; return 0; }
        var value = BitConverter.ToUInt16(data, pos);
        pos += 2;
        return value;
    }

    private static void WriteString(List<byte> buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        WriteU16(buffer, (ushort)(bytes.Length + 1));
        buffer.AddRange(bytes);
        buffer.Add(0);
    }

    private static void WriteI32(List<byte> buffer, int value) => buffer.AddRange(BitConverter.GetBytes(value));
    private static void WriteU32(List<byte> buffer, uint value) => buffer.AddRange(BitConverter.GetBytes(value));
    private static void WriteU16(List<byte> buffer, ushort value) => buffer.AddRange(BitConverter.GetBytes(value));

    private readonly record struct IncomingPacket(uint DeviceIndex, uint Id, byte[] Data);
}

internal sealed class OpenRgbController
{
    public uint Index;
    public int Type;
    public string Name = string.Empty;
    public string Vendor = string.Empty;
    public int LedCount;
    public int ActiveMode;
    public List<OpenRgbMode> Modes { get; } = new();
    public List<OpenRgbZone> Zones { get; } = new();
    public List<RgbColor> Colors { get; } = new();
}

internal sealed class OpenRgbMode
{
    public int Index;
    public string Name = string.Empty;
    public int Value;
    public uint Flags;
    public uint SpeedMin, SpeedMax, BrightnessMin, BrightnessMax;
    public uint ColorsMin, ColorsMax;
    public uint Speed, Brightness, Direction, ColorMode;
    public List<RgbColor> Colors { get; } = new();
}

internal sealed class OpenRgbZone
{
    public string Name = string.Empty;
    public int Type;
    public int LedCount;
}
