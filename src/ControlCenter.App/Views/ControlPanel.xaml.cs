using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ControlCenter.Core;
using ControlCenter.App.ViewModels;
using ControlCenter.Windows;
namespace ControlCenter.App.Views;
public partial class ControlPanel : Window
{
    private readonly PanelTransition transition = new();
    private bool exiting;
    private NativeTray? anchorTray;
    public bool SuppressDismiss { get; set; }
    public bool MotionEnabled { get; set; } = true;
    public bool CanHide { get; set; } = true;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;
    public void SetTrayAvailable(bool available)
    {
        CanHide = available; ShowInTaskbar = !available;
        ExitButton.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        WindowStyle = available ? WindowStyle.None : WindowStyle.SingleBorderWindow;
    }
    public PanelViewModel Model { get; }
    public PanelPhase Phase => transition.Phase;
    public ControlPanel(PanelViewModel model)
    {
        Model = model; InitializeComponent(); DataContext = model;
        ApplyModules();
        Model.PropertyChanged += ModelChanged;
        IsVisibleChanged += (_, _) => Model.SetVisible(IsVisible);
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (!IsActive && !SuppressDismiss) Dismiss(); });
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) => NativeWindow.Round(new WindowInteropHelper(this).Handle);
        SizeChanged += (_, _) => { if (IsVisible) KeepInWorkArea(); };
    }
    public void Present(NativeTray? tray = null)
    {
        anchorTray = tray;
        var placement = NativeWindow.Placement(tray);
        MaxHeight = Math.Max(120, (placement.Work.Bottom - placement.Work.Top) / placement.Scale - 24);
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // First move establishes target monitor DPI before WPF measures its content.
        if (!IsVisible) NativeWindow.Position(handle, placement.Work.Left + 12, placement.Work.Top + 12);
        UpdateLayout();
        Show();
        UpdateLayout();
        var source = PresentationSource.FromVisual(this);
        var scale = source?.CompositionTarget?.TransformToDevice.M11 ?? placement.Scale;
        var width = ActualWidth * scale; var height = ActualHeight * scale;
        var anchor = placement.Anchor;
        var x = anchor?.Right - width ?? placement.Work.Right - width - 12 * scale;
        var y = anchor is { } r && r.Bottom <= placement.Work.Top + 12 ? placement.Work.Top + 12 * scale : placement.Work.Bottom - height - 12 * scale;
        var target = PanelPlacement.Clamp(x, y, width, height,
            new(placement.Work.Left, placement.Work.Top, placement.Work.Right - placement.Work.Left, placement.Work.Bottom - placement.Work.Top), 12 * scale);
        NativeWindow.Position(handle, (int)target.X, (int)target.Y);
        NativeWindow.Foreground(handle); Activate();
        Animate(true);
        if (DeviceButton.IsVisible) DeviceButton.Focus(); else SettingsButton.Focus();
    }
    private void KeepInWorkArea()
    {
        var placement = NativeWindow.Placement(anchorTray);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? placement.Scale;
        var point = PointToScreen(new System.Windows.Point(0, 0));
        var target = PanelPlacement.Clamp(point.X, point.Y, ActualWidth * scale, ActualHeight * scale,
            new(placement.Work.Left, placement.Work.Top, placement.Work.Right - placement.Work.Left, placement.Work.Bottom - placement.Work.Top), 12 * scale);
        NativeWindow.Position(new WindowInteropHelper(this).Handle, (int)target.X, (int)target.Y);
    }
    public void RefreshPlacement()
    {
        if (!IsVisible || exiting) return;
        var placement = NativeWindow.Placement(anchorTray);
        MaxHeight = Math.Max(120, (placement.Work.Bottom - placement.Work.Top) / placement.Scale - 24);
        KeepInWorkArea();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshPlacement);
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Model.Modules)) ApplyModules(); }
    private void ApplyModules()
    {
        var cards = new Dictionary<string, FrameworkElement> { ["audio"] = AudioCard, ["power"] = PowerCard, ["awake"] = AwakeCard, ["proxy"] = ProxyCard, ["shortcuts"] = ShortcutsCard };
        ModuleHost.Children.Clear();
        foreach (string id in Model.Modules) { cards[id].Visibility = Visibility.Visible; ModuleHost.Children.Add(cards[id]); }
        foreach (var (id, card) in cards) if (!Model.Modules.Contains(id)) { card.Visibility = Visibility.Collapsed; ModuleHost.Children.Add(card); }
    }
    public void Toggle(NativeTray? tray)
    {
        if (transition.Phase is PanelPhase.Open or PanelPhase.Opening) Dismiss(); else Present(tray);
    }
    public void Dismiss() { if (CanHide && transition.Phase is not (PanelPhase.Closed or PanelPhase.Closing)) Animate(false); }
    private void Animate(bool opening)
    {
        var wasClosed = transition.Phase == PanelPhase.Closed;
        var token = transition.Request(opening);
        var transform = (TranslateTransform)ContentRoot.RenderTransform;
        var opacity = wasClosed && opening ? 0 : ContentRoot.Opacity;
        var y = wasClosed && opening ? 6 : transform.Y;
        ContentRoot.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        if (!MotionEnabled || !SystemParameters.ClientAreaAnimation)
        {
            ContentRoot.Opacity = 1; transform.Y = 0;
            transition.Complete(token); if (!opening) Hide(); return;
        }
        var duration = TimeSpan.FromMilliseconds(opening ? 140 : 120);
        var fade = new DoubleAnimation(opacity, opening ? 1 : 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            if (!transition.Complete(token)) return;
            ContentRoot.BeginAnimation(OpacityProperty, null); ContentRoot.Opacity = 1;
            transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = 0;
            if (!opening) Hide();
        };
        ContentRoot.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, opening ? 0 : 6, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (Model.DevicesOpen) { Model.DevicesOpen = false; DeviceButton.Focus(); }
        else Dismiss();
        e.Handled = true;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    private void Volume_Begin(object sender, MouseButtonEventArgs e) => Model.BeginVolumeGesture();
    private void Volume_End(object sender, MouseButtonEventArgs e) => Model.EndVolumeGesture();
    private void Volume_LostCapture(object sender, MouseEventArgs e) => Model.EndVolumeGesture();
    private void DeviceList_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { if (DeviceList.IsVisible) Dispatcher.BeginInvoke(() => DeviceList.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))); else if (IsVisible) DeviceButton.Focus(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; if (CanHide) Dismiss(); else ExitRequested?.Invoke(); }
        base.OnClosing(e);
    }
    public void Exit() { exiting = true; Model.PropertyChanged -= ModelChanged; Model.SetVisible(false); Model.Dispose(); Close(); }
}

