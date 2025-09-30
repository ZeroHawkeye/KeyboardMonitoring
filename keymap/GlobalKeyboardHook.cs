using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Threading;

namespace KeyboardMonitoring;

/// <summary>
/// Provides a global (system wide) low level keyboard hook for Windows.
/// Only active when constructed on Windows. Dispose to unhook.
/// </summary>
public sealed class GlobalKeyboardHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private LowLevelKeyboardProc? _proc; // Keep delegate alive
    private IntPtr _hookId = IntPtr.Zero;
    private bool _disposed;

    public event EventHandler<GlobalKeyEventArgs>? KeyEvent;

    public GlobalKeyboardHook()
    {
        if (!OperatingSystem.IsWindows())
            return; // silently do nothing on non-Windows

        _proc = HookCallback;
        _hookId = SetHook(_proc);
    }

    private IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        IntPtr hMod = GetModuleHandle(curModule.ModuleName);
        var hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, hMod, 0);
        return hook;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            int msg = wParam.ToInt32();
            if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                var state = msg switch
                {
                    WM_KEYDOWN => GlobalKeyState.KeyDown,
                    WM_KEYUP => GlobalKeyState.KeyUp,
                    WM_SYSKEYDOWN => GlobalKeyState.SysKeyDown,
                    WM_SYSKEYUP => GlobalKeyState.SysKeyUp,
                    _ => GlobalKeyState.Unknown
                };

                var modifiers = GetModifierString();
                var keyText = GetKeyText(data.vkCode);
                KeyEvent?.Invoke(this, new GlobalKeyEventArgs(DateTime.Now, data.vkCode, data.scanCode, data.flags, state, modifiers, keyText));
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static string GetKeyText(uint vkCode)
    {
        if (vkCode >= 0x20 && vkCode <= 0x7E)
        {
            char c = (char)vkCode;
            return c.ToString();
        }
        return string.Empty;
    }

    private static string GetModifierString()
    {
        var list = new List<string>();
        if (IsDown(0x10)) list.Add("Shift"); // VK_SHIFT
        if (IsDown(0x11)) list.Add("Ctrl");  // VK_CONTROL
        if (IsDown(0x12)) list.Add("Alt");   // VK_MENU
        if (IsDown(0x5B) || IsDown(0x5C)) list.Add("Win"); // VK_LWIN/VK_RWIN
        return list.Count == 0 ? "(none)" : string.Join('+', list);
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~GlobalKeyboardHook()
    {
        Dispose();
    }

    #region Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    #endregion
}

public enum GlobalKeyState
{
    Unknown,
    KeyDown,
    KeyUp,
    SysKeyDown,
    SysKeyUp
}

public sealed class GlobalKeyEventArgs : EventArgs
{
    public DateTime Timestamp { get; }
    public uint VirtualKeyCode { get; }
    public uint ScanCode { get; }
    public uint Flags { get; }
    public GlobalKeyState State { get; }
    public string Modifiers { get; }
    public string KeyText { get; }

    public GlobalKeyEventArgs(DateTime timestamp, uint vkCode, uint scanCode, uint flags, GlobalKeyState state, string modifiers, string keyText)
    {
        Timestamp = timestamp;
        VirtualKeyCode = vkCode;
        ScanCode = scanCode;
        Flags = flags;
        State = state;
        Modifiers = modifiers;
        KeyText = keyText;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(Timestamp.ToString("HH:mm:ss.fff")).Append("] ");
        sb.Append(State);
        sb.Append(" VK=0x").Append(VirtualKeyCode.ToString("X2"));
        if (!string.IsNullOrEmpty(KeyText))
            sb.Append(" ('").Append(KeyText).Append("')");
        sb.Append(" Scan=0x").Append(ScanCode.ToString("X2"));
        sb.Append(" Flags=0x").Append(Flags.ToString("X2"));
        sb.Append(" Mods=").Append(Modifiers);
        return sb.ToString();
    }
}

