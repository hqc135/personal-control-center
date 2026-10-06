using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Windows.Controls;
using ControlCenter.Core;
using ControlCenter.App.ViewModels;
using ControlCenter.Windows;
namespace ControlCenter.App.Views;
public partial class ControlPanel : Window
{
    private readonly PanelTransition transition = new();
    private bool exiting, userPlaced;
    private bool allFeatures, geometryRestored;
    private string layoutSignature = "";
    private NativeTray? anchorTray;
    public bool SuppressDismiss { get; set; }
    public bool IsPinned { get; private set; }
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
        Model.Desktop.PropertyChanged += DesktopChanged;
        Model.PropertyChanged += ModelChanged;
        IsVisibleChanged += (_, _) => Model.SetVisible(IsVisible);
        Deactivated += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { if (!IsActive && !SuppressDismiss && !IsPinned) Dismiss(); });
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeWindow.Round(handle);
            HwndSource.FromHwnd(handle)?.AddHook(WindowMessage);
        };
    }
    public void Present(NativeTray? tray = null)
    {
        anchorTray = tray;
        if (IsVisible && transition.Phase != PanelPhase.Closed)
        { Activate(); if (transition.Phase == PanelPhase.Closing) Animate(true); return; }
        if (MotionEnabled && SystemParameters.ClientAreaAnimation)
        { ContentRoot.Opacity = 0; ((TranslateTransform)ContentRoot.RenderTransform).Y = 4; }
        if (!geometryRestored)
        {
            geometryRestored = true;
            if (Model.Desktop.Preferences.Geometry is { IsValid: true } saved)
            {
                Width = saved.Width; Height = saved.Height;
                NativeWindow.Position(new WindowInteropHelper(this).EnsureHandle(), (int)saved.Left, (int)saved.Top);
                userPlaced = true;
            }
        }
        if (allFeatures || Model.Desktop.Preferences.Favorites.Any(x => x is "battery" or "media" or "microphone" or "brightness" or "connections")) _ = Model.Desktop.RefreshAsync();
        if (userPlaced)
        {
            Show(); RefreshPlacement(); Activate(); Animate(true); return;
        }
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
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0232)
        {
            userPlaced = true; RefreshPlacement();
            var point = PointToScreen(new System.Windows.Point());
            Model.Desktop.Save(Model.Desktop.Preferences with { Geometry = new(point.X, point.Y, ActualWidth, ActualHeight) });
        } // WM_EXITSIZEMOVE
        return 0;
    }
    private void KeepInWorkArea()
    {
        var placement = NativeWindow.Placement(anchorTray, userPlaced ? new WindowInteropHelper(this).Handle : 0);
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? placement.Scale;
        var point = PointToScreen(new System.Windows.Point(0, 0));
        var target = PanelPlacement.Clamp(point.X, point.Y, ActualWidth * scale, ActualHeight * scale,
            new(placement.Work.Left, placement.Work.Top, placement.Work.Right - placement.Work.Left, placement.Work.Bottom - placement.Work.Top), 12 * scale);
        NativeWindow.Position(new WindowInteropHelper(this).Handle, (int)target.X, (int)target.Y);
    }
    public void RefreshPlacement()
    {
        if (!IsVisible || exiting) return;
        var placement = NativeWindow.Placement(anchorTray, userPlaced ? new WindowInteropHelper(this).Handle : 0);
        MaxHeight = Math.Max(120, (placement.Work.Bottom - placement.Work.Top) / placement.Scale - 24);
        MaxWidth = Math.Max(MinWidth, (placement.Work.Right - placement.Work.Left) / placement.Scale - 24);
        KeepInWorkArea();
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RefreshPlacement);
    }
    private void ModelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(Model.Modules)) ApplyModules(); }
    private void DesktopChanged(object? sender, PropertyChangedEventArgs e) => ApplyModules();
    private void ApplyModules()
    {
        var cards = new Dictionary<string, FrameworkElement> { ["audio"] = AudioCard, ["power"] = PowerCard, ["awake"] = AwakeCard, ["proxy"] = ProxyCard, ["shortcuts"] = ShortcutsCard,
            ["battery"] = BatteryCard, ["microphone"] = MicrophoneCard, ["brightness"] = BrightnessCard, ["media"] = MediaCard, ["connections"] = ConnectionsCard, ["tools"] = ToolsCard };
        var order = Model.Modules.Concat(cards.Keys.Except(Model.Modules)).ToArray();
        var visible = order.Where(id => (allFeatures || Model.Desktop.Preferences.Favorites.Contains(id))
            && (id != "battery" || Model.Desktop.Battery.Length > 0) && (id != "media" || Model.Desktop.Media.Length > 0) && (id != "tools" || allFeatures)).ToArray();
        // Availability changes only visibility; ordinary refresh keeps visual children and focus.
        string signature = string.Join(',', order);
        if (signature != layoutSignature || ModuleHost.Children.Count == 0)
        {
            layoutSignature = signature;
            ModuleHost.Children.Clear();
            foreach (string id in order) ModuleHost.Children.Add(cards[id]);
        }
        foreach (var (id, card) in cards) card.Visibility = visible.Contains(id) ? Visibility.Visible : Visibility.Collapsed;
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
        var y = wasClosed && opening ? 4 : transform.Y;
        ContentRoot.BeginAnimation(OpacityProperty, null);
        transform.BeginAnimation(TranslateTransform.YProperty, null);
        ContentRoot.Opacity = opacity; transform.Y = y;
        if (!MotionEnabled || !SystemParameters.ClientAreaAnimation)
        {
            ContentRoot.Opacity = 1; transform.Y = 0;
            transition.Complete(token); if (!opening) Hide(); return;
        }
        var distance = Math.Clamp(opening ? 1 - opacity : opacity, 0, 1);
        var duration = TimeSpan.FromMilliseconds(Math.Max(1, (opening ? 140 : 90) * distance));
        var fade = new DoubleAnimation(opacity, opening ? 1 : 0, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        fade.Completed += (_, _) =>
        {
            if (!transition.Complete(token)) return;
            ContentRoot.BeginAnimation(OpacityProperty, null); ContentRoot.Opacity = 1;
            transform.BeginAnimation(TranslateTransform.YProperty, null); transform.Y = 0;
            if (!opening) Hide();
        };
        ContentRoot.BeginAnimation(OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, opening ? 0 : 4, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }, HandoffBehavior.SnapshotAndReplace);
    }
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (!string.IsNullOrEmpty(Model.SearchText)) { Model.SearchText = ""; ShortcutSearch.Focus(); e.Handled = true; return; }
        if (Model.DevicesOpen) { Model.DevicesOpen = false; DeviceButton.Focus(); }
        else Dismiss();
        e.Handled = true;
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private void Home_Click(object sender, RoutedEventArgs e) { allFeatures = false; HomeTab.Style = (Style)FindResource("PrimaryButton"); AllTab.Style = (Style)FindResource(typeof(Button)); ApplyModules(); PanelScroll.ScrollToTop(); }
    private void All_Click(object sender, RoutedEventArgs e) { allFeatures = true; AllTab.Style = (Style)FindResource("PrimaryButton"); HomeTab.Style = (Style)FindResource(typeof(Button)); Model.EnableAllModules(); ApplyModules(); PanelScroll.ScrollToTop(); _ = Model.Desktop.RefreshAsync(); }
    private void ResetGeometry_Click(object sender, RoutedEventArgs e)
    { Model.Desktop.Save(Model.Desktop.Preferences with { Geometry = null }); userPlaced = false; Width = 460; Height = 720; Present(anchorTray); }
    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var (id, label) in new[] { ("audio", "声音"), ("power", "电源"), ("awake", "保持唤醒"), ("proxy", "代理"), ("shortcuts", "常用入口"), ("battery", "电池"), ("media", "媒体"), ("microphone", "麦克风"), ("brightness", "亮度"), ("connections", "网络与蓝牙") })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = Model.Desktop.Preferences.Favorites.Contains(id), StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                var favorites = Model.Desktop.Preferences.Favorites.ToList();
                if (item.IsChecked) { if (!favorites.Contains(id)) favorites.Add(id); } else favorites.Remove(id);
                Model.Desktop.Save(Model.Desktop.Preferences with { Favorites = favorites.ToArray() });
                if (item.IsChecked) { Model.EnableAllModules(); _ = Model.Desktop.RefreshAsync(); }
            };
            menu.Items.Add(item);
        }
        bool old = SuppressDismiss; SuppressDismiss = true;
        menu.Closed += (_, _) => SuppressDismiss = old;
        menu.PlacementTarget = (UIElement)sender; menu.IsOpen = true;
    }
    private void OpenSelectedShortcut()
    {
        if (SearchResults.SelectedItem is ShortcutDefinition selected) Model.Launch.Execute(selected);
        else if (Model.FilteredShortcuts.FirstOrDefault() is { } first) Model.Launch.Execute(first);
    }
    private void ShortcutSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down) { SearchResults.Focus(); if (SearchResults.Items.Count > 0) SearchResults.SelectedIndex = Math.Max(0, SearchResults.SelectedIndex); e.Handled = true; }
        if (e.Key == Key.Enter) { OpenSelectedShortcut(); e.Handled = true; }
    }
    private void ShortcutResults_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { OpenSelectedShortcut(); e.Handled = true; } }
    private void ShortcutResults_DoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedShortcut();
    private void OpenShortcut_Click(object sender, RoutedEventArgs e) => OpenSelectedShortcut();
    private async void Output_Click(object sender, RoutedEventArgs e)
    {
        if (OutputPicker.SelectedItem is AudioDevice device) { await Model.SelectOutputAsync(device.Id); }
    }
    private async void Brightness_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id, Parent: DockPanel parent })
        { var slider = parent.Children.OfType<Slider>().First(); await Model.Desktop.SetBrightnessAsync(id, (int)Math.Round(slider.Value)); Model.Notice = Model.Desktop.Status; }
    }
    private async void Media_Click(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string action, DataContext: MediaSession session }) { await Model.Desktop.MediaAsync(session.Id, action); Model.Notice = Model.Desktop.Status; } }
    private void Scenes_Click(object sender, RoutedEventArgs e) => ShowTool(new SceneWindow(Model.Desktop, Model.PowerChoices));
    private void Updates_Click(object sender, RoutedEventArgs e) => ShowTool(new UpdateWindow());
    private void ShowTool(Window window)
    {
        bool old = SuppressDismiss; SuppressDismiss = true; window.Owner = this;
        window.Closed += (_, _) => { SuppressDismiss = old; if (!exiting) Activate(); }; window.Show();
    }
    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        IsPinned = !IsPinned;
        PinButton.Content = IsPinned ? "取消固定" : "固定";
        PinButton.ToolTip = IsPinned ? "已固定：切换窗口时保持打开；Esc 仍可收起" : "切换到其他窗口时保持面板打开";
        PinButton.SetResourceReference(BackgroundProperty, IsPinned ? "AccentSurfaceBrush" : "SurfaceBrush");
    }
    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
    private void Volume_Begin(object sender, MouseButtonEventArgs e) => Model.BeginVolumeGesture();
    private void Volume_End(object sender, MouseButtonEventArgs e) => Model.EndVolumeGesture();
    private void Volume_LostCapture(object sender, MouseEventArgs e) => Model.EndVolumeGesture();
    private void DeviceList_VisibleChanged(object sender, DependencyPropertyChangedEventArgs e) { if (!DeviceList.IsVisible && IsVisible) DeviceButton.Focus(); }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!exiting) { e.Cancel = true; if (CanHide) Dismiss(); else ExitRequested?.Invoke(); }
        base.OnClosing(e);
    }
    public void Exit()
    {
        if (exiting) return;
        exiting = true;
        Model.Desktop.PropertyChanged -= DesktopChanged; Model.PropertyChanged -= ModelChanged;
        Model.SetVisible(false); Model.Dispose();
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != 0) HwndSource.FromHwnd(handle)?.RemoveHook(WindowMessage);
        if (IsLoaded) Close();
    }
}
