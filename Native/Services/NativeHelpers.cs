using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace Native.Services;

internal static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    /// <summary>Barra escura e cantos arredondados do Windows 11.</summary>
    public static void ApplyDarkRounded(Window window) => ApplyFrame(window, ThemeManager.IsDark);

    public static void ApplyFrame(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    /// <summary>Janela que aparece sem roubar o foco nem ocupar o Alt+Tab (para os banners).</summary>
    public static void MakeNonActivating(Window window) => SetActivatable(window, false);

    /// <summary>Permite (ou não) que a janela receba foco — necessário para digitar uma resposta no banner.</summary>
    public static void SetActivatable(Window window, bool activatable)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int style = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        style = activatable ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }
}

internal static class TrayIconFactory
{
    /// <summary>Ícone do app (embutido no executável), no tamanho certo para a bandeja.</summary>
    public static Drawing.Icon Create()
    {
        try
        {
            var stream = typeof(TrayIconFactory).Assembly.GetManifestResourceStream("Native.Assets.Native.ico");
            if (stream is not null)
            {
                using (stream)
                {
                    int size = System.Windows.Forms.SystemInformation.SmallIconSize.Width;
                    return new Drawing.Icon(stream, size, size);
                }
            }
        }
        catch
        {
            // Sem o recurso: desenha um ícone simples.
        }
        return Drawn();
    }

    private static Drawing.Icon Drawn()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var body = RoundedRect(new Drawing.RectangleF(8.5f, 2f, 15f, 28f), 4.5f);
            using var pen = new Drawing.Pen(Drawing.Color.FromArgb(10, 132, 255), 2.6f);
            g.DrawPath(pen, body);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private static Drawing2D.GraphicsPath RoundedRect(Drawing.RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
