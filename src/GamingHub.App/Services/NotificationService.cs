using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;
using Windows.UI.Notifications;

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
    /// <summary>Stabile AppUserModelID für unverpackte Desktop-Toasts.</summary>
    public const string ToastAppId = "GamingHub.Desktop";

    public event Action<NotificationKind>? Activated;

    /// <summary>True, wenn native Windows-Toasts auf diesem System möglich sind.</summary>
    public bool NativeSupported { get; private set; }

    /// <summary>True, wenn der Benutzer Windows-Benachrichtigungen erlaubt hat.</summary>
    public bool NativeEnabled { get; private set; }

    /// <summary>Für die Einstellungen: aktueller Status der Windows-Toasts.</summary>
    public string NativeStatus { get; private set; } = "Native Windows-Benachrichtigungen werden geprüft…";

    /// <summary>Beschreibt das konkrete Hindernis (falls vorhanden) inklusive
    /// der Windows-Einstellungsseite, auf der es behoben werden kann.</summary>
    public string NotificationProblem { get; private set; } = string.Empty;

    /// <summary>Direkter Sprung zur passenden Windows-Einstellungsseite.</summary>
    public string NotificationSettingsUri { get; private set; } = "ms-settings:notifications";

    public bool HasNotificationProblem => !string.IsNullOrWhiteSpace(NotificationProblem);

    /// <summary>Registriert die AUMID und einen Startmenü-Link, damit Windows
    /// Toasts von unverpackten Apps überhaupt zustellt.</summary>
    public static void Initialize()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(ToastAppId);

            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Classes\AppUserModelId\{ToastAppId}");
            if (key is not null)
            {
                key.SetValue("DisplayName", "Gaming Hub");
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
                key.SetValue("IconBackgroundColor", "0E1024");
                string? exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue("IconUri", exe);
            }

            EnsureStartMenuShortcut();
        }
        catch
        {
            // Ohne Registrierung funktioniert weiterhin die In-App-Anzeige.
        }
    }

    /// <summary>Prüft, ob Windows-Benachrichtigungen erlaubt sind, und
    /// registriert bei Bedarf die AppUserModelId für unverpackte Apps.</summary>
    public void RefreshNativeStatus()
    {
        Initialize();

        NotificationProblem = string.Empty;
        NotificationSettingsUri = "ms-settings:notifications";

        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            NativeSupported = true;
            NativeEnabled = notifier.Setting == NotificationSetting.Enabled;

            if (notifier.Setting == NotificationSetting.DisabledForApplication || !IsAppNotificationsEnabled())
            {
                NativeEnabled = false;
                NotificationProblem =
                    "Gaming Hub ist in den Windows-Benachrichtigungseinstellungen deaktiviert. Aktiviere dort den Schalter für Gaming Hub.";
                NotificationSettingsUri = "ms-settings:notifications";
            }
            else if (notifier.Setting == NotificationSetting.DisabledForUser || !IsGlobalNotificationsEnabled())
            {
                NativeEnabled = false;
                NotificationProblem =
                    "Benachrichtigungen sind für dein Windows-Benutzerkonto komplett deaktiviert. Aktiviere sie in den Windows-Einstellungen.";
                NotificationSettingsUri = "ms-settings:notifications";
            }
            else if (notifier.Setting == NotificationSetting.DisabledByGroupPolicy)
            {
                NativeEnabled = false;
                NotificationProblem =
                    "Benachrichtigungen wurden durch eine Richtlinie (Gruppenrichtlinie/Administrator) blockiert.";
                NotificationSettingsUri = "ms-settings:notifications";
            }
            else if (notifier.Setting == NotificationSetting.DisabledByManifest)
            {
                NativeEnabled = false;
                NotificationProblem =
                    "Windows blockiert Benachrichtigungen für diese App über das App-Manifest.";
                NotificationSettingsUri = "ms-settings:notifications";
            }
            else
            {
                var state = GetUserNotificationState();
                switch (state)
                {
                    case UserNotificationState.QuietTime:
                        NotificationProblem =
                            "Der Fokusassistent (Nicht stören) ist aktiv. Toasts werden deshalb unterdrückt.";
                        NotificationSettingsUri = "ms-settings:notifications";
                        break;
                    case UserNotificationState.RunningD3dFullScreen:
                        NotificationProblem =
                            "Ein Spiel läuft im Vollbild. Windows unterdrückt Benachrichtigungen während Vollbild-Anwendungen.";
                        NotificationSettingsUri = "ms-settings:notifications";
                        break;
                    case UserNotificationState.PresentationMode:
                        NotificationProblem =
                            "Der Präsentationsmodus ist aktiv. Windows unterdrückt Benachrichtigungen.";
                        NotificationSettingsUri = "ms-settings:notifications";
                        break;
                    case UserNotificationState.Busy:
                    case UserNotificationState.NotPresent:
                        NotificationProblem =
                            "Windows meldet den Status „beschäftigt/abwesend“. Benachrichtigungen können dadurch unterdrückt werden.";
                        NotificationSettingsUri = "ms-settings:notifications";
                        break;
                }
            }

            if (!NativeEnabled)
            {
                NativeStatus = string.IsNullOrWhiteSpace(NotificationProblem)
                    ? "Windows-Benachrichtigungen sind deaktiviert."
                    : NotificationProblem;
            }
            else if (NotificationProblem.Length > 0)
            {
                NativeStatus = "Windows-Benachrichtigungen sind aktiv, aber aktuell gestört: " + NotificationProblem;
            }
            else
            {
                NativeStatus = "Windows-Benachrichtigungen sind aktiv.";
            }

            if (!IsNotificationSoundEnabledByWindows())
                NativeStatus += " Systemtöne für Benachrichtigungen sind in Windows ausgeschaltet.";
        }
        catch (Exception ex)
        {
            NativeSupported = false;
            NativeEnabled = false;
            string detail = ex.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = ex.GetType().Name + " (0x" + ex.HResult.ToString("X8") + ")";
            NotificationProblem =
                "Native Windows-Toasts sind nicht verfügbar. Die App zeigt Benachrichtigungen weiterhin als In-App-Hinweis an (" +
                detail + ").";
            NotificationSettingsUri = "ms-settings:notifications";
            NativeStatus = NotificationProblem;
        }
    }

    private enum UserNotificationState
    {
        NotPresent = 1,
        Busy = 2,
        RunningD3dFullScreen = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState state);

    private static UserNotificationState GetUserNotificationState()
    {
        try
        {
            return SHQueryUserNotificationState(out var state) == 0
                ? state
                : UserNotificationState.AcceptsNotifications;
        }
        catch
        {
            return UserNotificationState.AcceptsNotifications;
        }
    }

    private static bool IsGlobalNotificationsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            return key?.GetValue("NOC_GLOBAL_SETTING_TOASTS_ENABLED") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsAppNotificationsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings\{ToastAppId}");
            if (key is null)
                return true;
            return key.GetValue("Enabled") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    private static bool IsNotificationSoundEnabledByWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Notifications\Settings");
            return key?.GetValue("NOC_GLOBAL_SETTING_ALLOW_NOTIFICATION_SOUND") is not int v || v != 0;
        }
        catch
        {
            return true;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    private static void EnsureStartMenuShortcut()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !System.IO.File.Exists(exe))
                return;

            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string folder = System.IO.Path.Combine(programs, "Gaming Hub");
            System.IO.Directory.CreateDirectory(folder);
            string linkPath = System.IO.Path.Combine(folder, "Gaming Hub Benachrichtigungen.lnk");

            var link = (IShellLinkW)new ShellLink();
            link.SetPath(exe);
            link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(exe) ?? string.Empty);
            link.SetIconLocation(exe, 0);
            link.SetDescription("Gaming Hub");

            var store = (IPropertyStore)link;
            var key = PKEY_AppUserModel_ID;
            var value = new PropVariant
            {
                vt = 31, // VT_LPWSTR
                pointerValue = Marshal.StringToCoTaskMemUni(ToastAppId)
            };
            try
            {
                store.SetValue(ref key, ref value);
                store.Commit();
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.pointerValue);
            }

            ((IPersistFile)link).Save(linkPath, true);
        }
        catch
        {
            // Der Startmenü-Link ist nur für native Toasts nötig.
        }
    }

    private static readonly PropertyKey PKEY_AppUserModel_ID = new(
        new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid formatId;
        public int propertyId;

        public PropertyKey(Guid formatId, int propertyId)
        {
            this.formatId = formatId;
            this.propertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr pointerValue;
        public int pointerValue2;
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        uint GetCount();
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    /// <summary>Zeigt eine echte Windows-Testbenachrichtigung.</summary>
    public bool ShowTest()
    {
        RefreshNativeStatus();
        if (TryShowNative("Gaming Hub", "Testbenachrichtigung – Windows-Benachrichtigungen funktionieren."))
            return true;

        Show("Gaming Hub", "Testbenachrichtigung – In-App-Anzeige.");
        return false;
    }

    private readonly Queue<(NotificationKind Kind, string Title, string Body)> _pending = new();
    private bool _showing;
    private Window? _toast;
    private DispatcherTimer? _hideTimer;
    private bool _ringActive;
    private string? _ringFile;

    public NotificationService()
    {
        RefreshNativeStatus();
        try
        {
            ToastNotificationManagerCompat.OnActivated += _ =>
                Dispatcher.UIThread.Post(() => Activated?.Invoke(NotificationKind.NewMessage));
        }
        catch
        {
            // Aktivierung ist optional; Anzeige funktioniert auch ohne.
        }
    }

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

    /// <summary>General-purpose toast (e.g. failed update at startup).</summary>
    public void Show(string title, string body)
        => Enqueue(NotificationKind.NewMessage, title, body);

    private void Enqueue(NotificationKind kind, string title, string body)
    {
        if (!AppServices.Settings.Settings.DesktopNotificationsEnabled)
            return;

        // Zuerst eine echte Windows-Benachrichtigung versuchen. Nur wenn das
        // System sie nicht erlaubt/unterstützt, wird die In-App-Anzeige genutzt.
        if (TryShowNative(title, body))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            _pending.Enqueue((kind, title, body));
            ShowNext();
        });
    }

    private bool TryShowNative(string title, string body)
    {
        if (!NativeSupported)
            return false;

        try
        {
            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            NativeEnabled = notifier.Setting == NotificationSetting.Enabled;
            if (!NativeEnabled)
                return false;

            var content = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .GetToastContent();
            var toast = new ToastNotification(content.GetXml())
            {
                Tag = "gaminghub",
                Group = "gaminghub"
            };
            notifier.Show(toast);

            if (SoundEnabled && AppServices.Settings.Settings.NotificationSoundEnabled)
                PlaySystemSound("SystemAsterisk");
            return true;
        }
        catch
        {
            return false;
        }
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