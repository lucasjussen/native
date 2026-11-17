using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Native.Models;

namespace Native.Controls;

/// <summary>Ícone de app no formato "squircle" do iOS, com letra colorida enquanto o ícone real não carrega.</summary>
public sealed class AppIconView : Grid
{
    public static readonly DependencyProperty AppProperty = DependencyProperty.Register(
        nameof(App), typeof(AppInfo), typeof(AppIconView),
        new PropertyMetadata(null, (d, e) => ((AppIconView)d).OnAppChanged((AppInfo?)e.OldValue, (AppInfo?)e.NewValue)));

    public AppInfo? App
    {
        get => (AppInfo?)GetValue(AppProperty);
        set => SetValue(AppProperty, value);
    }

    private readonly Border _fallback = new();
    private readonly Border _image = new();
    private readonly TextBlock _initial = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
        Foreground = Brushes.White,
    };

    public AppIconView()
    {
        _fallback.Child = _initial;
        Children.Add(_fallback);
        Children.Add(_image);
        SizeChanged += (_, _) => UpdateGeometry();
        Unloaded += (_, _) => { if (App is not null) App.PropertyChanged -= OnAppPropertyChanged; };
        Loaded += (_, _) => { if (App is not null) { App.PropertyChanged -= OnAppPropertyChanged; App.PropertyChanged += OnAppPropertyChanged; } };
    }

    private void UpdateGeometry()
    {
        var radius = new CornerRadius(ActualWidth * 0.225);
        _fallback.CornerRadius = radius;
        _image.CornerRadius = radius;
        if (ActualWidth > 0) _initial.FontSize = ActualWidth * 0.46;
    }

    private void OnAppChanged(AppInfo? oldApp, AppInfo? newApp)
    {
        if (oldApp is not null) oldApp.PropertyChanged -= OnAppPropertyChanged;
        if (newApp is not null) newApp.PropertyChanged += OnAppPropertyChanged;
        Refresh();
    }

    private void OnAppPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        var app = App;
        if (app is null) return;
        _fallback.Background = app.FallbackBrush;
        _initial.Text = app.Initial;
        _image.Background = app.Icon is null ? null : new ImageBrush(app.Icon) { Stretch = Stretch.UniformToFill };
        ToolTip = app.DisplayName;
    }
}
