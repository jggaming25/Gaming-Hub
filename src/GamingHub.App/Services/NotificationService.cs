using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace GamingHub.App.Services;

public enum NotificationKind
{
    NewMessage,
    IncomingCall,
    MissedCall
}

/// <summary>
/// Shows desktop notifications (overlay toasts with the Gaming Hub logo) for
/// new chat messages, incoming or missed calls. Phase 6 chat/WebRTC calls call
/// into NewMessage / IncomingCall / MissedCall.
/// </summary>
public sealed class NotificationService
{
    public event Action<NotificationKind>? Activated;

    private readonly Queue<(NotificationKind Kind, string Title, string Body)> _pending = new();
    private bool _showing;
    private Window? _toast;
    private DispatcherTimer? _hideTimer;
    private bool _ringActive;
    private string? _ringFile;

    public void NewMessage(string chatName, string sender, string message)
    {
        Enqueue(NotificationKind.NewMessage,
            $"{sender} · {chatName}",
            message);
    }

    public void IncomingCall(string caller)
    {
        Enqueue(NotificationKind.IncomingCall, "Eingehender Anruf", caller);
    }

    public void MissedCall(string caller)
    {
        Enqueue(NotificationKind.MissedCall, "Verpasster Anruf", caller);
    }

    private void Enqueue(NotificationKind kind, string title, string body)
    {
        if (!AppServices.Settings.Settings.DesktopNotificationsEnabled)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _pending.Enqueue((kind, title, body));
            ShowNext();
        });
    }

    private void ShowNext()
    {
        if (_showing || _pending.Count == 0)
            return;

        var (kind, title, body) = _pending.Dequeue();
        _showing = true;
        ShowToast(kind, title, body);
    }

    private void ShowToast(NotificationKind kind, string title, string body)
    {
        try
        {
            _hideTimer?.Stop();

            var winsColors = kind switch
            {
                NotificationKind.NewMessage => "#22D3EE",
                NotificationKind.IncomingCall => "#4ADE80",
                _ => "#F87171"
            };
            var accent = new SolidColorBrush(Color.Parse(winsColors));
            double duration = kind switch
            {
                NotificationKind.IncomingCall => 15,
                NotificationKind.MissedCall => 8,
                _ => 6
            };

            var win = new Window
            {
                Width = 360,
                Height = 96,
                ShowInTaskbar = false,
                Topmost = true,
                CanResize = false,
                Background = new SolidColorBrush(Color.Parse("#0E1024"))
            };

            var root = BuildToastRoot(accent, title, body, () => ActivateAndClose(kind, win), () => CloseToast(win));

            win.Content = root;

            win.PointerPressed += (_, e) => { e.Handled = true; ActivateAndClose(kind, win); };

            win.Show();

            var wa = win.Screens.ScreenFromWindow(win)?.WorkingArea;
            if (wa is { } area)
                win.Position = new PixelPoint(area.X + area.Width - 360 - 16, area.Y + area.Height - 96 - 16);

            if (SoundEnabled)
                PlayKindSound(kind);

            _toast = win;
            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(duration) };
            _hideTimer.Tick += (_, _) => CloseToast(win);
            _hideTimer.Start();
        }
        catch
        {
            _showing = false;
        }
    }

    private static Grid BuildToastRoot(SolidColorBrush accent, string title, string body,
        Action activate, Action close)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*,Auto") };

        var accentBar = new Border { Background = accent, Width = 4, CornerRadius = new CornerRadius(2) };
        Grid.SetColumn(accentBar, 0);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(12, 8, 10, 8) };
        Image? logo = null;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://GamingHub.App/Assets/logo.png"));
            logo = new Image
            {
                Source = new Bitmap(stream),
                Width = 44,
                Height = 44,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Top
            };
        }
        catch
        {
            logo = new Image();
        }
        Grid.SetColumn(logo, 0);
        logo.Margin = new Thickness(0, 0, 12, 0);

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleText = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var bodyText = new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#A6ADC8")),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 34
        };
        texts.Children.Add(titleText);
        texts.Children.Add(bodyText);
        Grid.SetColumn(texts, 1);

        content.Children.Add(logo);
        content.Children.Add(texts);

        var closeBtn = new TextBlock
        {
            Text = "\u2715",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.Parse("#8B91AC")),
            Margin = new Thickness(0, 6, 8, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        closeBtn.PointerPressed += (_, e) => { e.Handled = true; close(); };
        Grid.SetColumn(closeBtn, 2);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#12142E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#2A3350")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(1),
            Child = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*") }
        };
        (card.Child as Grid)!.Children.Add(accentBar);
        (card.Child as Grid)!.Children.Add(content);
        Grid.SetColumn(content, 1);

        grid.Children.Add(card);

        grid.PointerPressed += (_, e) => { e.Handled = true; activate(); };
        return grid;
    }

    private void ActivateAndClose(NotificationKind kind, Window win)
    {
        Activated?.Invoke(kind);
        CloseToast(win);
    }

    private void CloseToast(Window? win)
    {
        StopRingSound();
        _hideTimer?.Stop();
        if (win is not null && win.IsVisible)
            win.Close();
        if (win is not null && ReferenceEquals(win, _toast))
            _toast = null;
        _showing = false;
        ShowNext();
    }

    private bool SoundEnabled => AppServices.Settings.Settings.NotificationSoundEnabled;

    private void PlayKindSound(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.NewMessage:
                PlaySystemSound("SystemAsterisk");
                break;
            case NotificationKind.MissedCall:
                PlaySystemSound("SystemExclamation");
                break;
            case NotificationKind.IncomingCall:
                PlayRingSound();
                break;
        }
    }

    private void PlayRingSound()
    {
        try
        {
            _ringFile ??= EnsureRingFile();
            PlaySoundFn(_ringFile, IntPtr.Zero, SND_ASYNC | SND_LOOP | SND_FILENAME | SND_NOSTOP);
            _ringActive = true;
        }
        catch
        {
            // ignore
        }
    }

    private void StopRingSound()
    {
        if (!_ringActive)
            return;
        try
        {
            PlaySoundFn(null, IntPtr.Zero, 0);
        }
        catch { }
        _ringActive = false;
    }

    private static string EnsureRingFile()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GamingHub");
        System.IO.Directory.CreateDirectory(dir);
        string file = System.IO.Path.Combine(dir, "incoming_ring.wav");
        if (!System.IO.File.Exists(file))
            System.IO.File.WriteAllBytes(file, BuildRingWav());
        return file;
    }

    /// <summary>Generates a polite two-tone ring with a pause so it loops naturally.</summary>
    private static byte[] BuildRingWav()
    {
        const int sampleRate = 22050;
        const int channels = 1;
        const double gapSeconds = 0.6;
        var samples = new List<short>();

        void AddTone(double freq, double seconds, double amp)
        {
            int n = (int)(seconds * sampleRate);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sampleRate;
                double env = amp * Math.Min(1.0, t * 60.0) * Math.Min(1.0, (seconds - t) * 60.0);
                short v = (short)(Math.Sin(2 * Math.PI * freq * t) * 0.45 * env * short.MaxValue);
                samples.Add(v);
            }
        }

        // Warble 440/480 Hz like a phone ringer.
        for (int ring = 0; ring < 2; ring++)
        {
            while (samples.Count % sampleRate != 0) samples.Add(0);
            AddTone(440, 0.25, 0.7);
            AddTone(480, 0.25, 0.7);
            for (int i = 0; i < (int)(gapSeconds * sampleRate); i++)
                samples.Add(0);
        }

        int dataSize = samples.Count * 2;
        int fileSize = 44 + dataSize;
        var wav = new byte[fileSize];

        void WriteAscii(int offset, string s) => System.Text.Encoding.ASCII.GetBytes(s, 0, s.Length, wav, offset);
        void WriteU16(int offset, ushort v) { wav[offset] = (byte)(v & 0xFF); wav[offset + 1] = (byte)((v >> 8) & 0xFF); }
        void WriteU32(int offset, uint v)
        {
            wav[offset] = (byte)(v & 0xFF);
            wav[offset + 1] = (byte)((v >> 8) & 0xFF);
            wav[offset + 2] = (byte)((v >> 16) & 0xFF);
            wav[offset + 3] = (byte)((v >> 24) & 0xFF);
        }

        WriteAscii(0, "RIFF");
        WriteU32(4, (uint)(fileSize - 8));
        WriteAscii(8, "WAVE");
        WriteAscii(12, "fmt ");
        WriteU32(16, 16);
        WriteU16(20, 1); // PCM
        WriteU16(22, channels);
        WriteU32(24, (uint)sampleRate);
        WriteU32(28, (uint)(sampleRate * channels * 2));
        WriteU16(32, (ushort)(channels * 2));
        WriteU16(34, 16);
        WriteAscii(36, "data");
        WriteU32(40, (uint)dataSize);

        int offset = 44;
        foreach (short s in samples)
        {
            wav[offset] = (byte)(s & 0xFF);
            wav[offset + 1] = (byte)((s >> 8) & 0xFF);
            offset += 2;
        }

        return wav;
    }

    private static void PlaySystemSound(string alias)
    {
        try
        {
            PlaySoundFn(alias, IntPtr.Zero, SND_ALIAS | SND_ASYNC | SND_NOSTOP);
        }
        catch { }
    }

    private const uint SND_ALIAS = 0x00010000;
    private const uint SND_ASYNC = 0x00000001;
    private const uint SND_LOOP = 0x00000008;
    private const uint SND_NOSTOP = 0x00000010;
    private const uint SND_FILENAME = 0x00020000;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySoundFn(string? pszSound, IntPtr hmod, uint fdwSound);

    /// <summary>Stops ring sound when the app shuts down.</summary>
    public void Shutdown()
    {
        StopRingSound();
        _hideTimer?.Stop();
        try
        {
            if (_toast?.IsVisible == true)
                _toast.Close();
        }
        catch { }
    }
}