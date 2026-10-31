using System.Runtime.InteropServices;

namespace GamingHub.App.Services;

/// <summary>
/// Sperrt die Windows-Tasten und typische Ausweichkombinationen, solange eine
/// vollflächige Bildschirmzeit-Sperre aktiv ist.
/// </summary>
public static class KeyboardLockService
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_MENU = 0x12;
    private const int VK_CONTROL = 0x11;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    private static readonly object Gate = new();
    private static readonly ManualResetEventSlim Started = new(false);
    private static HookProc? _hookProc;
    private static IntPtr _hook;
    private static Thread? _thread;
    private static uint _threadId;
    private static volatile bool _enabled;

    public static bool IsEnabled => _enabled;

    public static void SetFullScreenLock(bool enabled)
    {
        lock (Gate)
        {
            if (enabled == _enabled)
                return;

            _enabled = enabled;
            if (enabled)
                StartHook();
            else
                StopHook();
        }
    }

    private static void StartHook()
    {
        if (_thread is not null)
            return;

        Started.Reset();
        _hookProc = HookCallback;
        _thread = new Thread(HookThread)
        {
            IsBackground = true,
            Name = "GamingHub Keyboard Lock"
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!Started.Wait(TimeSpan.FromSeconds(2)) || _hook == IntPtr.Zero)
        {
            _enabled = false;
            StopHook();
            LogService.Info("Die Windows-Tasten konnten für die Bildschirmzeit-Sperre nicht deaktiviert werden.");
        }
    }

    private static void StopHook()
    {
        if (_threadId != 0)
            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _thread?.Join(500);
        _thread = null;
        _threadId = 0;
        _hook = IntPtr.Zero;
        _hookProc = null;
    }

    private static void HookThread()
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc!, GetModuleHandle(null), 0);
        Started.Set();

        if (_hook == IntPtr.Zero)
            return;

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            if (_hook != IntPtr.Zero)
                UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
            _enabled = false;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled)
        {
            int message = wParam.ToInt32();
            if (message is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
                if (IsBlocked(info.vkCode))
                    return new IntPtr(1);
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsBlocked(int virtualKey)
    {
        if (virtualKey is VK_LWIN or VK_RWIN)
            return true;

        if (virtualKey == VK_ESCAPE && IsKeyDown(VK_CONTROL))
            return true;

        if (virtualKey == VK_TAB && IsKeyDown(VK_MENU))
            return true;

        return false;
    }

    private static bool IsKeyDown(int virtualKey) =>
        (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }
}
