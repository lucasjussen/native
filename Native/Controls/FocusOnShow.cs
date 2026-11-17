using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Native.Controls;

/// <summary>Coloca o cursor no campo assim que ele aparece (ex.: ao tocar em "Responder").</summary>
public static class FocusOnShow
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(FocusOnShow), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.IsVisibleChanged -= OnVisibleChanged;
        if ((bool)e.NewValue) element.IsVisibleChanged += OnVisibleChanged;
    }

    private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not UIElement element || !(bool)e.NewValue) return;
        element.Dispatcher.InvokeAsync(() =>
        {
            element.Focus();
            Keyboard.Focus(element);
            if (element is TextBoxBase box) box.SelectAll();
        }, DispatcherPriority.Input);
    }

    /// <summary>O clique veio de dentro de um campo de texto ou botão? (para não disparar o clique do cartão)</summary>
    public static bool IsFromInteractive(object? source)
    {
        var d = source as DependencyObject;
        while (d is not null)
        {
            if (d is TextBoxBase or ButtonBase) return true;
            d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }
}
