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

    // ------------------------------------------------------------ Liquid Glass: desfoque real do que está atrás

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;   // desfoque simples: leve e sem o atraso ao arrastar do "acrylic"
    private const int DWMNCRP_ENABLED = 2;
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);

    /// <summary>Precisa do Windows 11: é ele que arredonda as pontas do desfoque junto com a janela.</summary>
    public static bool BlurSupported => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Janelas que participam do vidro fosco (a janela grande transparente, o Mini e o mini player).</summary>
    private static readonly System.Collections.Generic.HashSet<Window> GlassWindows = new();

    public static void RegisterGlass(Window window)
    {
        GlassWindows.Add(window);
        window.Closed += (_, _) => GlassWindows.Remove(window);
    }

    /// <summary>
    /// Liga/desliga o desfoque atrás da janela. Ligado, o Windows 11 arredonda a janela (8 px), desenha a
    /// borda fina e a sombra dele — por isso essas janelas tiram a própria margem e sombra nesse modo.
    /// </summary>
    public static void ApplyBlur(Window window, bool on)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        on &= BlurSupported;
        if (on)
        {
            int policy = DWMNCRP_ENABLED;
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
            int round = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int border = DWMWA_COLOR_DEFAULT;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            int dark = ThemeManager.IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        SetAccent(hwnd, on ? ACCENT_ENABLE_BLURBEHIND : ACCENT_DISABLED);
        if (!on && window.AllowsTransparency) SuppressFrame(window);
    }

    private static void SetAccent(IntPtr hwnd, int state)
    {
        var accent = new AccentPolicy { AccentState = state };
        int size = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch (Exception)
        {
            // Windows sem essa função: fica só translúcido, sem desfoque.
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public static void ApplyFrame(Window window, bool dark)
    {
        if (GlassWindows.Contains(window))
        {
            ApplyBlur(window, ThemeManager.BlurActive);
            return;
        }
        // Janelas transparentes (banner, mini player) desenham o próprio cartão. Se o Windows 11
        // arredondar e sombrear a janela inteira, aparece um "fantasma" em volta do cartão.
        if (window.AllowsTransparency)
        {
            SuppressFrame(window);
            return;
        }
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int on = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        int round = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    private const int DWMWA_NCRENDERING_POLICY = 2;
    private const int DWMNCRP_DISABLED = 1;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    /// <summary>Tira moldura, borda, sombra e cantos que o Windows 11 põe em volta da janela.</summary>
    public static void SuppressFrame(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int policy = DWMNCRP_DISABLED;
        DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref policy, sizeof(int));
        int corner = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int border = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
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
