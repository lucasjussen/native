using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Native.Services;
using Native.ViewModels;

namespace Native;

/// <summary>Mini player "picture in picture" para áudio: sempre no topo, arrastável, lembra onde ficou.</summary>
public partial class PipWindow : Window
{
    private readonly MainViewModel _vm;
    public event Action? OpenRequested;

    public PipWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        SourceInitialized += (_, _) => WindowEffects.SuppressFrame(this);
        Loaded += (_, _) => PlaceOnScreen();
        LocationChanged += (_, _) => { if (IsLoaded) SavePosition(); };
        Closing += OnClosing;
    }

    private void PlaceOnScreen()
    {
        var area = SystemParameters.WorkArea;
        double left = _vm.PipLeft ?? area.Right - ActualWidth - 8;
        double top = _vm.PipTop ?? area.Bottom - ActualHeight - 8;
        // Se a tela mudou (outro monitor, resolução), traz de volta para dentro.
        Left = Math.Clamp(left, area.Left - 10, Math.Max(area.Left, area.Right - ActualWidth + 10));
        Top = Math.Clamp(top, area.Top - 6, Math.Max(area.Top, area.Bottom - ActualHeight + 14));
    }

    private void SavePosition()
    {
        _vm.PipLeft = Math.Round(Left);
        _vm.PipTop = Math.Round(Top);
    }

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        // Só arrasta se o clique não foi num botão ou numa barra.
        if (e.OriginalSource is DependencyObject d && IsInteractive(d)) return;
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private static bool IsInteractive(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
        {
            if (cur is System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.Slider) return true;
            if (cur is PipWindow) break;
        }
        return false;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => _vm.IsPipOpen = false;

    private void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsPipOpen) _vm.IsPipOpen = false;
    }
}
