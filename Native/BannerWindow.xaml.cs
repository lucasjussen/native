using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Native.Models;
using Native.Services;
using Native.ViewModels;

namespace Native;

/// <summary>
/// Banner que desliza no canto superior direito, no estilo das notificações do macOS.
/// Se chegarem várias de uma vez, elas entram em fila em vez de se atropelarem.
/// </summary>
public partial class BannerWindow : Window
{
    private const int MaxQueue = 6;

    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _autoHide = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly Queue<PhoneNotification> _queue = new();
    private PhoneNotification? _current;
    private bool _hiding;

    public event Action? OpenRequested;

    public BannerWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _autoHide.Tick += (_, _) => HideAnimated();
        SourceInitialized += (_, _) =>
        {
            WindowEffects.MakeNonActivating(this);
            WindowEffects.SuppressFrame(this);
        };
        MouseEnter += (_, _) => _autoHide.Stop();
        MouseLeave += (_, _) => { if (_current is { IsCall: false, IsReplying: false }) RestartTimer(); };
    }

    public void Present(PhoneNotification notification)
    {
        Core.Log.Info("Banner", notification.IsCall ? "Ligação recebida" : "Notificação exibida");
        // Ligação fura a fila; o resto espera o banner atual sair.
        if (IsVisible && _current is not null && !_hiding && !notification.IsCall)
        {
            if (_queue.Count >= MaxQueue) _queue.Dequeue();
            _queue.Enqueue(notification);
            if (_current is { IsCall: false }) _autoHide.Interval = TimeSpan.FromSeconds(3); // acelera a fila
            return;
        }
        ShowNotification(notification);
    }

    public void DismissIfShowing(uint uid)
    {
        if (_current?.Uid == uid) HideAnimated();
    }

    private void ShowNotification(PhoneNotification notification)
    {
        if (_current is not null) _current.PropertyChanged -= OnCurrentChanged;
        notification.PropertyChanged += OnCurrentChanged;
        _current = notification;
        DataContext = notification;
        _hiding = false;

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width;
        Top = area.Top;

        if (!IsVisible)
        {
            Slide.BeginAnimation(TranslateTransform.XProperty, null);
            Slide.X = Width;
            Show();
        }
        Animate(0, 340, null);

        _autoHide.Interval = TimeSpan.FromSeconds(_queue.Count > 0 ? 3 : 6);
        if (notification.IsCall) _autoHide.Stop();
        else RestartTimer();
    }

    private void RestartTimer()
    {
        _autoHide.Stop();
        _autoHide.Start();
    }

    private void HideAnimated()
    {
        _autoHide.Stop();
        if (!IsVisible || _hiding) return;
        _hiding = true;
        Animate(Width, 220, () =>
        {
            if (!_hiding) return;      // outra notificação entrou no meio da animação
            Hide();
            if (_current is not null)
            {
                _current.PropertyChanged -= OnCurrentChanged;
                _current.IsReplying = false;
            }
            _current = null;
            _hiding = false;
            WindowEffects.SetActivatable(this, false);

            while (_queue.Count > 0)
            {
                var next = _queue.Dequeue();
                ShowNotification(next);
                break;
            }
        });
    }

    private void Animate(double to, int milliseconds, Action? completed)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = to == 0 ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        if (completed is not null) animation.Completed += (_, _) => completed();
        Slide.BeginAnimation(TranslateTransform.XProperty, animation);
    }

    /// <summary>
    /// Enquanto a pessoa digita uma resposta, o banner precisa aceitar foco e não pode sumir sozinho.
    /// Depois de enviar, ele fecha logo em seguida, como no macOS.
    /// </summary>
    private void OnCurrentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PhoneNotification n || n != _current) return;

        if (e.PropertyName == nameof(PhoneNotification.IsReplying) && n.IsReplying)
        {
            _queue.Clear();
            _autoHide.Stop();
            WindowEffects.SetActivatable(this, true);
            Activate();
        }
        else if (e.PropertyName == nameof(PhoneNotification.ReplySent) && n.ReplySent)
        {
            WindowEffects.SetActivatable(this, false);
            _autoHide.Interval = TimeSpan.FromSeconds(1.5);
            RestartTimer();
        }
        else if (e.PropertyName == nameof(PhoneNotification.IsReplying) && !n.IsReplying && !n.ReplySent)
        {
            WindowEffects.SetActivatable(this, false);
            RestartTimer();
        }
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (Controls.FocusOnShow.IsFromInteractive(e.OriginalSource)) return;
        _queue.Clear();
        OpenRequested?.Invoke();
        HideAnimated();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => HideAnimated();

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) _vm.CopyCode(_current);
        RestartTimer();
    }

    private void Answer_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) _vm.PerformAction(_current, positive: true);
        HideAnimated();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not null) _vm.PerformAction(_current, positive: false);
        HideAnimated();
    }
}
