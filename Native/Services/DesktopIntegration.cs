using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Native.Services;

/// <summary>Atalho global do sistema (funciona com qualquer app em foco).</summary>
internal sealed class GlobalHotKey : IDisposable
{
    public const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;
    private const int Id = 0xC0DE;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly IntPtr _hwnd;
    private readonly HwndSource? _source;
    private readonly Action _callback;

    public bool IsRegistered { get; }

    public GlobalHotKey(Window window, uint modifiers, uint virtualKey, Action callback)
    {
        _callback = callback;
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(Hook);
        IsRegistered = RegisterHotKey(_hwnd, Id, modifiers | ModNoRepeat, virtualKey);
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == Id)
        {
            _callback();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(_hwnd, Id);
        _source?.RemoveHook(Hook);
    }
}

/// <summary>Som curto de notificação, discreto como o do macOS.</summary>
internal static class NotificationSound
{
    private static readonly string[] Candidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify System Generic.wav"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Notify Messaging.wav"),
    };

    private static SoundPlayer? _player;

    public static void Play()
    {
        try
        {
            if (_player is null)
            {
                foreach (var path in Candidates)
                {
                    if (!File.Exists(path)) continue;
                    _player = new SoundPlayer(path);
                    _player.Load();
                    break;
                }
            }

            if (_player is not null) _player.Play();
            else SystemSounds.Asterisk.Play();
        }
        catch
        {
            // Som é opcional.
        }
    }
}

internal static class ClipboardHelper
{
    /// <summary>A área de transferência às vezes está ocupada por outro app; tenta algumas vezes.</summary>
    public static bool TrySetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(40);
            }
            catch
            {
                return false;
            }
        }
        return false;
    }
}

/// <summary>Cores escuras para o menu da bandeja (WinForms), combinando com o resto do app.</summary>
internal sealed class DarkMenuColors : Forms.ProfessionalColorTable
{
    private static readonly Drawing.Color Background = Drawing.Color.FromArgb(44, 44, 46);
    private static readonly Drawing.Color Highlight = Drawing.Color.FromArgb(10, 132, 255);
    private static readonly Drawing.Color Line = Drawing.Color.FromArgb(72, 72, 74);

    public override Drawing.Color ToolStripDropDownBackground => Background;
    public override Drawing.Color ImageMarginGradientBegin => Background;
    public override Drawing.Color ImageMarginGradientMiddle => Background;
    public override Drawing.Color ImageMarginGradientEnd => Background;
    public override Drawing.Color MenuBorder => Line;
    public override Drawing.Color MenuItemBorder => Highlight;
    public override Drawing.Color MenuItemSelected => Highlight;
    public override Drawing.Color MenuItemSelectedGradientBegin => Highlight;
    public override Drawing.Color MenuItemSelectedGradientEnd => Highlight;
    public override Drawing.Color MenuItemPressedGradientBegin => Highlight;
    public override Drawing.Color MenuItemPressedGradientEnd => Highlight;
    public override Drawing.Color SeparatorDark => Line;
    public override Drawing.Color SeparatorLight => Background;
    public override Drawing.Color CheckBackground => Background;
    public override Drawing.Color CheckSelectedBackground => Highlight;
    public override Drawing.Color CheckPressedBackground => Highlight;

    public static void Apply(Forms.ContextMenuStrip menu)
    {
        menu.Renderer = new Forms.ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = true };
        menu.BackColor = Background;
        menu.ForeColor = Drawing.Color.White;
        menu.Font = new Drawing.Font("Segoe UI", 9.5f);
        menu.ShowImageMargin = true;
    }
}
