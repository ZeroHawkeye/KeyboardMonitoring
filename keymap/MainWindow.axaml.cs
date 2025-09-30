using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Text;

namespace keymap;

public partial class MainWindow : Window
{
    private GlobalKeyboardHook? _hook;
    private readonly LinkedList<string> _lines = new();
    private const int MaxLines = 1000; // rolling buffer size

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _hook = new GlobalKeyboardHook();
        _hook.KeyEvent += OnGlobalKeyEvent;
        AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] 全局键盘钩子已初始化 (Windows={(OperatingSystem.IsWindows())})");
    }

    private void OnGlobalKeyEvent(object? sender, GlobalKeyEventArgs e)
    {
        // Ensure UI update on UI thread
        Dispatcher.UIThread.Post(() => AppendLine(e.ToString()));
    }

    private void AppendLine(string line)
    {
        _lines.AddLast(line);
        if (_lines.Count > MaxLines)
            _lines.RemoveFirst();
        if (LogBox != null)
        {
            LogBox.Text = string.Join(Environment.NewLine, _lines);
            LogBox.CaretIndex = LogBox.Text!.Length; // scroll to end
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_hook != null)
        {
            _hook.KeyEvent -= OnGlobalKeyEvent;
            _hook.Dispose();
            _hook = null;
        }
    }
}