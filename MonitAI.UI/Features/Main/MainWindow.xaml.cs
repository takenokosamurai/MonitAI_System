using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop; // 必須
using System.Runtime.InteropServices;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MonitAI.Core;           // 必須
using MonitAI.UI.Features.MonitoringOverlay;
using System.Windows.Media.Animation;
using System.Diagnostics;

namespace MonitAI.UI.Features.Main
{
    public partial class MainWindow : FluentWindow
    {
        // アニメーションを無効化して同期的に最終値を適用するデバッグ用フラグ
        // false にするとアニメーションを無効化します（テスト用）。
        // アニメーション有効フラグ（Windows11タイマー風の滑らかさを付与）
        private bool _animationsEnabled = true;

        private double _restoredWidth;
        private double _restoredHeight;
        private double _restoredTop;
        private double _restoredLeft;
        private WindowState _restoredState;
        private ResizeMode _restoredResizeMode;
        private WindowStyle _restoredWindowStyle;
        private double _restoredMinWidth;
        private double _restoredMinHeight;
        private double _restoredMaxWidth;
        private double _restoredMaxHeight;
        private bool _restoredTopmost;
        private bool _restoredShowInTaskbar;
        private bool _restoredExtendsContentIntoTitleBar;
        private Visibility _restoredTitleBarVisibility;
        private Brush? _restoredBackground;

        private MonitAI.UI.Features.Setup.SetupPage? _setupPage;
        private MonitAI.UI.Features.Settings.SettingsPage? _settingsPage;
        private MonitAI.UI.Features.MonitoringOverlay.MonitoringOverlay? _monitoringOverlay;

        // アニメーション中フラグ（再入禁止）
        private bool _isAnimating = false;

        // 監視中にメイン画面を真っ黒にするための以前の背景を保持
        private Brush? _monitoringPrevWindowBackground;
        private Brush? _monitoringPrevPageContentBackground;
        private Brush? _monitoringPrevRootNavBackground;

        // 監視中フラグ
        private bool _isMonitoring = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var snackbarService = new Wpf.Ui.SnackbarService();
            snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);

            var contentDialogService = new Wpf.Ui.ContentDialogService();
            contentDialogService.SetDialogHost(RootContentDialogPresenter);

            _setupPage = new MonitAI.UI.Features.Setup.SetupPage(snackbarService, contentDialogService);
            _setupPage.StartMonitoringRequested += OnStartMonitoring;

            _settingsPage = new MonitAI.UI.Features.Settings.SettingsPage();

            _monitoringOverlay = new MonitAI.UI.Features.MonitoringOverlay.MonitoringOverlay();
            _monitoringOverlay.ToggleMiniModeRequested += OnToggleMiniModeClick;
            _monitoringOverlay.DragMoveRequested += OnDragMoveWindow;
            _monitoringOverlay.StopMonitoringRequested += OnStopMonitoring;
            MonitoringOverlayContainer.Content = _monitoringOverlay;

            NavigateToSetup();
            try { WindowState = WindowState.Maximized; } catch { }
        }

        // ▼▼▼ フック登録 ▼▼▼
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource? source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
        }

        /// <summary>
        /// OSからのウィンドウメッセージを直接監視・ブロックする。
        /// ショートカットやシステムメニューからの操作をここで防ぐ。
        /// </summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!_isMonitoring) return IntPtr.Zero;

            if (msg == NativeMethods.WM_SYSCOMMAND)
            {
                int command = wParam.ToInt32() & 0xFFF0;

                // 1. 閉じる・最大化は常にブロック
                if (command == NativeMethods.SC_CLOSE || command == NativeMethods.SC_MAXIMIZE)
                {
                    handled = true;
                    return IntPtr.Zero;
                }

                bool isMini = _monitoringOverlay?.IsMiniMode == true;

                if (isMini)
                {
                    // 2. ミニモード: リサイズ(SC_SIZE)をブロック、最小化(SC_MINIMIZE)は許可
                    if (command == NativeMethods.SC_SIZE)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
                }
                else
                {
                    // 3. 通常モード: 最小化は許可する（タスクバーからの最小化をブロックしない）
                    // リサイズは許可のまま。特別なブロックは行わない。
                }
            }
            return IntPtr.Zero;
        }

        private void RootNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs e)
        {
            if (sender.SelectedItem is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                switch (tag)
                {
                    case "Setup": NavigateToSetup(); break;
                    case "Settings": NavigateToSettings(); break;
                }
            }
        }

        private void NavigationViewItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString();
                switch (tag)
                {
                    case "Setup": NavigateToSetup(); break;
                    case "Settings": NavigateToSettings(); break;
                }
            }
        }

        private void NavigateToSetup()
        {
            if (_setupPage != null && PageContent != null)
                PageContent.Content = _setupPage;
        }

        private void NavigateToSettings()
        {
            if (_settingsPage != null && PageContent != null)
                PageContent.Content = _settingsPage;
        }

        // ▼▼▼ 監視開始時の処理 ▼▼▼
        private void OnStartMonitoring(MonitoringSession session)
        {
            _monitoringOverlay?.Initialize(session);

            // 保存しておく: ウィンドウ／コンテンツの背景を黒にして、アニメ中に後ろが見えないようにする
            try
            {
                _monitoringPrevWindowBackground = Background;
                _monitoringPrevPageContentBackground = PageContent?.Background as Brush;
                _monitoringPrevRootNavBackground = RootNavigation?.Background as Brush;
            }
            catch { }

            Background = Brushes.Black;
            if (PageContent != null) PageContent.Background = Brushes.Black;
            if (RootNavigation != null) RootNavigation.Background = Brushes.Black;

            if (RootNavigation != null) RootNavigation.Visibility = Visibility.Collapsed;
            if (MonitoringOverlayContainer != null) MonitoringOverlayContainer.Visibility = Visibility.Visible;

            _isMonitoring = true;

            // 監視モードではウィンドウを 860x480 に固定して中央に表示する
            try
            {
                WindowState = WindowState.Normal;
                double targetW = 860;
                double targetH = 480;
                Width = targetW;
                Height = targetH;
                MaxWidth = targetW; MaxHeight = targetH;

                var area = SystemParameters.WorkArea;
                Left = area.Left + (area.Width - targetW) / 2.0;
                Top = area.Top + (area.Height - targetH) / 2.0;
            }
            catch { }

            // 通常モードの設定を適用
            // リサイズ許可(CanResize)、タイトルバーのボタンは閉じる・最小化・最大化すべて非表示
            ApplyWindowMode(isMini: false);
        }

        private void StopAndResetSession()
        {
            _monitoringOverlay?.StopSession();

            RootNavigation.Visibility = Visibility.Visible;
            MonitoringOverlayContainer.Visibility = Visibility.Collapsed;

            _isMonitoring = false;

            // 制限解除
            ResetWindowControls();

            // 監視モードにより黒にしていた背景を復元
            try
            {
                if (_monitoringPrevWindowBackground != null) Background = _monitoringPrevWindowBackground;
                else Background = (Brush)FindResource("ApplicationBackgroundBrush");

                if (PageContent != null)
                    PageContent.Background = _monitoringPrevPageContentBackground ?? (Brush)FindResource("ApplicationBackgroundBrush");

                if (RootNavigation != null)
                    RootNavigation.Background = _monitoringPrevRootNavBackground ?? (Brush)FindResource("ApplicationBackgroundBrush");
            }
            catch { }

            if (_monitoringOverlay?.IsMiniMode == true)
            {
                RestoreWindow();
                _monitoringOverlay?.ToggleMode();
            }

            // 監視を終了したらウィンドウは全画面（最大化）で開く
            try
            {
                // 制約を解除して最大化
                MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
                WindowState = WindowState.Maximized;
            }
            catch { }
        }

        private void OnStopMonitoring()
        {
            StopAndResetSession();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isMonitoring)
            {
                e.Cancel = true;
                return;
            }
            _setupPage?.SaveUserData();
        }

        private void OnToggleThemeClick(object sender, RoutedEventArgs e)
        {
            var currentTheme = ApplicationThemeManager.GetAppTheme();
            ApplicationThemeManager.Apply(
                currentTheme == ApplicationTheme.Light ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }

        private void OnDragMoveWindow(object? sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // ▼▼▼ ミニモード切り替え処理 ▼▼▼
        private async void OnToggleMiniModeClick(object? sender, EventArgs e)
        {
            if (_monitoringOverlay == null) return;
            if (_isAnimating) return;

            _isAnimating = true;
            try
            {
                bool willBecomeMini = !_monitoringOverlay.IsMiniMode;

                if (willBecomeMini)
                {
                    // 視覚的なプリエフェクト: 全画面を縮小せずブラー＋暗転＋少し移動
                    Debug.WriteLine("[Toggle] willBecomeMini: pre-effect start");
                    await AnimateOverlayPreEffect(320);
                    Debug.WriteLine("[Toggle] willBecomeMini: pre-effect end");

                    // 実際のモード切替とウィンドウ状態保存/変更
                    _monitoringOverlay.ToggleMode();
                    // === ミニモードへ遷移 ===
                    SaveWindowState();

                    if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;

                    WindowStyle = WindowStyle.None;

                    // 余白を消すための設定
                    ExtendsContentIntoTitleBar = false;

                    ResizeMode = ResizeMode.NoResize;
                    Topmost = true;
                    ShowInTaskbar = true;

                    // 最小サイズ制限を解除
                    MinWidth = 0;
                    MinHeight = 0;

                    // サイズを180x180に固定
                    var dpiScale = GetDpiScale();
                    double miniWidth = 180;
                    double miniHeight = 180;

                    // ネイティブでリサイズ/配置（再描画抑制）してから WPF プロパティを同期
                    try
                    {
                        var area = SystemParameters.WorkArea;
                        double targetLeft = area.Right - miniWidth - (20 / dpiScale.X);
                        double targetTop = area.Bottom - miniHeight - (20 / dpiScale.Y);
                        SetWindowBoundsNoRedraw(miniWidth, miniHeight, targetLeft, targetTop);
                        Width = miniWidth;
                        Height = miniHeight;
                        MaxWidth = miniWidth;
                        MaxHeight = miniHeight;

                        // 背景を透明に
                        Background = Brushes.Transparent;

                        // 位置も同期
                        Left = targetLeft;
                        Top = targetTop;
                    }
                    catch
                    {
                        Width = miniWidth;
                        Height = miniHeight;
                        MaxWidth = miniWidth;
                        MaxHeight = miniHeight;
                        Background = Brushes.Transparent;
                        var area = SystemParameters.WorkArea;
                        Left = area.Right - miniWidth - (20 / dpiScale.X);
                        Top = area.Bottom - miniHeight - (20 / dpiScale.Y);
                    }

                    MainTitleBar.Visibility = Visibility.Collapsed;

                    ApplyWindowMode(isMini: true);

                    // ミニモードではオーバーレイのスケールをリセットして
                    // ウィンドウのサイズに合わせる（アニメでScaleが保持されるのを防ぐ）
                    try
                    {
                        // スケール操作は不要のため削除
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Opacity = 1.0;
                        }
                    }
                    catch { }
                    // ミニ遷移完了時に Translate.Y を滑らかに 0 に戻す（プリエフェクトで下がったままにならないように）
                    try
                    {
                        Debug.WriteLine("[Toggle] willBecomeMini: post-mini translate reset start");
                        await AnimateOverlayScaleOpacity(1.0, 1.0, 200, fromTranslate: null, toTranslate: 0);
                        Debug.WriteLine("[Toggle] willBecomeMini: post-mini translate reset end");
                    }
                    catch { }
                }
                else
                {
                    // --- 復帰時の二段アニメーション ---
                    // 1) 画面が後ろに吸い込まれるように少し縮小（プリアニメ）
                    Debug.WriteLine("[Toggle] restore: pre-effect start");
                    await AnimateOverlayPreEffect(320);
                    Debug.WriteLine("[Toggle] restore: pre-effect end");

                    // 2) 新フロー: 画面を真っ黒にしてからサイズ変更 → 表示 → アニメ
                    Debug.WriteLine("[Toggle] restore: starting black->resize->reveal flow");
                    try
                    {
                        // a) BlackCoverWindow は使用しない（黒フラッシュ回避のため）

                        // b) 同期的にウィンドウのサイズ等を復元してレンダリングを待つ
                        Debug.WriteLine("[Toggle] restore: before RestoreWindowSync (using BlackCoverWindow)");
                        RestoreWindowSync();
                        Debug.WriteLine("[Toggle] restore: after RestoreWindowSync");

                        // Debug: dump window/restore background and state
                        try
                        {
                            Debug.WriteLine($"[Debug] before Wait: _restoredBackground={( _restoredBackground==null?"null":_restoredBackground.ToString())} Background={(Background==null?"null":Background.ToString())} WindowState={WindowState} Topmost={Topmost} ShowInTaskbar={ShowInTaskbar}");
                        }
                        catch { }

                        // b2) ウィンドウ状態が実際に復元されるまで待機してから黒を解除する
                        try
                        {
                            Debug.WriteLine("[Toggle] restore: waiting for WindowState to settle");
                            await WaitForWindowState(_restoredState, 2000);
                        }
                        catch { }

                        try
                        {
                            Debug.WriteLine($"[Debug] after Wait: WindowState={WindowState} Background={(Background==null?"null":Background.ToString())} _restoredBackground={( _restoredBackground==null?"null":_restoredBackground.ToString())}");
                        }
                        catch { }

                        // 強制レンダーでサイズ/レイアウト反映を確実にする
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                        // c) 内部コンテンツを通常モード状態へ切り替える（まだ黒覆いあり）
                        _monitoringOverlay.ToggleMode();
                        Debug.WriteLine("[Toggle] restore: after ToggleMode (content switched, black cover visible)");

                        // e) 表示直前に背景を元に戻し、コンテンツを即時表示（不透明）にする
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                            MonitoringOverlayContainer.Opacity = 1.0;
                        }

                        // レンダー待ち
                        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                        try
                        {
                            Debug.WriteLine($"[Debug] before reveal: Window.Background={(Background==null?"null":Background.ToString())} MonitoringOverlayContainer.Background={(MonitoringOverlayContainer?.Background==null?"null":MonitoringOverlayContainer.Background.ToString())} MonitoringOverlayContainer.Opacity={(MonitoringOverlayContainer==null?"null":MonitoringOverlayContainer.Opacity.ToString())} WindowState={WindowState} Topmost={Topmost}");
                        }
                        catch { }
                        // 背景を確実に不透明にしておく
                        try
                        {
                            var appBrush = (Brush)FindResource("ApplicationBackgroundBrush");
                            Background = appBrush;
                            if (MonitoringOverlayContainer != null)
                                MonitoringOverlayContainer.Background = appBrush;
                            _restoredBackground = appBrush;
                        }
                        catch { }

                        // f) 最終アニメーションでコンテンツを滑らかに表示
                        Debug.WriteLine("[Toggle] restore: before final reveal animation");
                        // スケール操作は不要のため削除
                        // 復帰時は上から下へ自然に落ちてくる表現にする（開始位置は現在のYを使う）
                        await AnimateOverlayScaleOpacity(0.98, 1.0, 320, fromTranslate: null, toTranslate: 0);
                        Debug.WriteLine("[Toggle] restore: after final reveal animation");

                        // g) 終了処理: 最終値をセット
                        // スケール操作は不要のため削除
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Opacity = 1.0;
                            MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[Toggle] restore: black->resize flow failed: " + ex.Message);
                        try
                        {
                            if (MonitoringOverlayContainer != null)
                                MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                        }
                        catch { }
                    }

                    // タイトルバー設定を戻す
                    ExtendsContentIntoTitleBar = true;

                    MaxWidth = double.PositiveInfinity;
                    MaxHeight = double.PositiveInfinity;

                    Background = (Brush)FindResource("ApplicationBackgroundBrush");

                    MainTitleBar.Visibility = Visibility.Visible;

                    ApplyWindowMode(isMini: false);

                    // 3) 全画面復帰時: 一瞬黒を表示してからフェードイン（ポップインを廃止）
                    try
                    {
                        // 既に BlackCoverWindow で黒を表示しており、カバーを閉じた後は
                        // オーバーレイ本体をアプリケーション背景にして透明状態からフェードインします。
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                            // Opacity は既に 0.0 にセットされている想定なので変更しない
                        }

                        await AnimateOverlayScaleOpacity(1.0, 1.0, 320, fromTranslate: null, toTranslate: 0);

                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Opacity = 1.0;
                            MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                        }

                        // BlackCoverWindow を使わないため何もしない
                    }
                    catch
                    {
                        try
                        {
                            if (MonitoringOverlayContainer != null)
                                MonitoringOverlayContainer.Background = (Brush)FindResource("ApplicationBackgroundBrush");
                            if (_monitoringOverlay != null)
                                _monitoringOverlay.Visibility = Visibility.Visible;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            finally
            {
                _isAnimating = false;
            }
        }
        private (double X, double Y) GetDpiScale()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var matrix = source.CompositionTarget.TransformToDevice;
                return (matrix.M11, matrix.M22);
            }
            return (1.0, 1.0);
        }

        private void SaveWindowState()
        {
            // 保存: ユーザーが通常モードで使っていたウィンドウ状態を丸ごと退避
            _restoredWidth = Width;
            _restoredHeight = Height;
            _restoredTop = Top;
            _restoredLeft = Left;
            _restoredState = WindowState;
            _restoredResizeMode = ResizeMode;
            _restoredWindowStyle = WindowStyle;

            _restoredMinWidth = MinWidth;
            _restoredMinHeight = MinHeight;
            _restoredMaxWidth = MaxWidth;
            _restoredMaxHeight = MaxHeight;
            _restoredTopmost = Topmost;
            _restoredShowInTaskbar = ShowInTaskbar;
            _restoredExtendsContentIntoTitleBar = ExtendsContentIntoTitleBar;
            _restoredTitleBarVisibility = MainTitleBar?.Visibility ?? Visibility.Visible;
            // If we're in monitoring mode we may have overridden the window Background to black
            // to hide content behind the overlay. In that case use the previously saved
            // monitoring previous background as the restored background to avoid restoring
            // a temporary black background that causes a flash.
            if (_isMonitoring && _monitoringPrevWindowBackground != null)
            {
                _restoredBackground = _monitoringPrevWindowBackground;
            }
            else
            {
                _restoredBackground = Background;
            }

            // 防御策: 透明なブラシを誤って保存してしまうと復元時に
            // 一瞬透過状態になり黒（デスクトップ）が見える原因となる。
            // そのため保存された背景が透明（または透明度0）なら
            // アプリケーション既定の背景で上書きする。
            try
            {
                if (_restoredBackground is SolidColorBrush scb)
                {
                    if (scb.Color.A == 0 || scb.Color == Colors.Transparent)
                    {
                        _restoredBackground = (Brush)FindResource("ApplicationBackgroundBrush");
                    }
                }
                else if (_restoredBackground == null)
                {
                    _restoredBackground = (Brush)FindResource("ApplicationBackgroundBrush");
                }
            }
            catch { }
        }

        private void RestoreWindow()
        {
            // 復元はレイアウト競合を避けるためディスパッチする
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // 一時的にノーマルにしてサイズ適用してから元状態へ戻す（Maximized対応）
                try
                {
                    // 一時解除
                    Topmost = false;

                    // まずウィンドウスタイル／リサイズ設定を戻す
                    WindowStyle = _restoredWindowStyle;
                    ResizeMode = _restoredResizeMode;
                    ExtendsContentIntoTitleBar = _restoredExtendsContentIntoTitleBar;

                    // WindowState が Maximized の場合は一旦 Normal にする
                    if (_restoredState == WindowState.Maximized)
                    {
                        WindowState = WindowState.Normal;
                    }

                    // 制約を戻す
                    MinWidth = _restoredMinWidth;
                    MinHeight = _restoredMinHeight;
                    MaxWidth = _restoredMaxWidth;
                    MaxHeight = _restoredMaxHeight;

                    // サイズと位置を復元
                    Width = double.IsNaN(_restoredWidth) || _restoredWidth <= 0 ? Width : _restoredWidth;
                    Height = double.IsNaN(_restoredHeight) || _restoredHeight <= 0 ? Height : _restoredHeight;
                    Left = double.IsNaN(_restoredLeft) ? Left : _restoredLeft;
                    Top = double.IsNaN(_restoredTop) ? Top : _restoredTop;

                    // 背景とタイトルバー表示
                    Background = _restoredBackground ?? (Brush)FindResource("ApplicationBackgroundBrush");
                    MainTitleBar.Visibility = _restoredTitleBarVisibility;

                    // 最後にウィンドウ状態とトップモストを戻す
                    WindowState = _restoredState;
                    Topmost = _restoredTopmost;
                    ShowInTaskbar = _restoredShowInTaskbar;
                }
                catch
                {
                    // 復元失敗時は最低限の復元を試みる
                    WindowState = WindowState.Normal;
                    MinWidth = 0; MinHeight = 0; MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
                    Background = (Brush)FindResource("ApplicationBackgroundBrush");
                    MainTitleBar.Visibility = Visibility.Visible;
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // 同期的にウィンドウ復元を行い、描画完了を待ちたい場合に呼ぶ
        private void RestoreWindowSync()
        {
            try
            {
                Topmost = false;

                WindowStyle = _restoredWindowStyle;
                ResizeMode = _restoredResizeMode;
                ExtendsContentIntoTitleBar = _restoredExtendsContentIntoTitleBar;

                if (_restoredState == WindowState.Maximized)
                {
                    WindowState = WindowState.Normal;
                }

                MinWidth = _restoredMinWidth;
                MinHeight = _restoredMinHeight;
                MaxWidth = _restoredMaxWidth;
                MaxHeight = _restoredMaxHeight;

                // ウィンドウのサイズ／位置をネイティブで適用（再描画を抑制）
                try
                {
                    double applyWidth = double.IsNaN(_restoredWidth) || _restoredWidth <= 0 ? Width : _restoredWidth;
                    double applyHeight = double.IsNaN(_restoredHeight) || _restoredHeight <= 0 ? Height : _restoredHeight;
                    double applyLeft = double.IsNaN(_restoredLeft) ? Left : _restoredLeft;
                    double applyTop = double.IsNaN(_restoredTop) ? Top : _restoredTop;
                    SetWindowBoundsNoRedraw(applyWidth, applyHeight, applyLeft, applyTop);
                    // さらに WPF プロパティも更新して内部状態を一致させる
                    Width = applyWidth;
                    Height = applyHeight;
                    Left = applyLeft;
                    Top = applyTop;
                }
                catch { }

                Background = _restoredBackground ?? (Brush)FindResource("ApplicationBackgroundBrush");
                MainTitleBar.Visibility = _restoredTitleBarVisibility;

                // 強制的にレンダリングを優先させる
                UpdateLayout();
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

                WindowState = _restoredState;
                Topmost = _restoredTopmost;
                ShowInTaskbar = _restoredShowInTaskbar;
            }
            catch
            {
                WindowState = WindowState.Normal;
                MinWidth = 0; MinHeight = 0; MaxWidth = double.PositiveInfinity; MaxHeight = double.PositiveInfinity;
                Background = (Brush)FindResource("ApplicationBackgroundBrush");
                MainTitleBar.Visibility = Visibility.Visible;
            }
        }

        // 指定の WindowState になるまで待ちます（タイムアウト付き）
        private Task<bool> WaitForWindowState(WindowState targetState, int timeoutMs = 2000)
        {
            var tcs = new TaskCompletionSource<bool>();

            EventHandler handler = null!;
            handler = (s, e) =>
            {
                try
                {
                    if (WindowState == targetState)
                    {
                        tcs.TrySetResult(true);
                        this.StateChanged -= handler;
                    }
                }
                catch { }
            };

            this.StateChanged += handler;

            // 既に目的の状態なら即時完了
            if (WindowState == targetState)
            {
                this.StateChanged -= handler;
                return Task.FromResult(true);
            }

            // タイムアウト
            Task.Delay(timeoutMs).ContinueWith(_ =>
            {
                tcs.TrySetResult(WindowState == targetState);
                try { this.StateChanged -= handler; } catch { }
            });

            return tcs.Task;
        }

        // --- Native helpers to adjust window bounds without forcing immediate redraw ---
        private const uint SWP_NOREDRAW = 0x0008;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        private void SetWindowBoundsNoRedraw(double width, double height, double left, double top)
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                IntPtr hwnd = helper.Handle;
                if (hwnd == IntPtr.Zero) return;

                int x = (int)Math.Round(left);
                int y = (int)Math.Round(top);
                int cx = (int)Math.Round(width);
                int cy = (int)Math.Round(height);

                uint flags = SWP_NOREDRAW | SWP_NOZORDER | SWP_NOACTIVATE;
                SetWindowPos(hwnd, IntPtr.Zero, x, y, cx, cy, flags);
            }
            catch { }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (RootNavigation != null && e.NewSize.Width < 800)
            {
                RootNavigation.IsPaneOpen = false;
            }
        }

        // ▼▼▼ ウィンドウ制御の実装 ▼▼▼

        /// <summary>
        /// モードに応じてWPFのResizeModeと、タイトルバーボタンの表示を切り替える
        /// </summary>
        private void ApplyWindowMode(bool isMini)
        {
            if (isMini)
            {
                // ミニモード: リサイズ不可、最小化可
                // (WPFのResizeMode.CanMinimizeを使うとリサイズ枠が消える)
                this.ResizeMode = ResizeMode.CanMinimize;

                // タイトルバー自体が非表示(Collapsed)なのでボタン操作は不要
            }
            else
            {
                // 通常モード: リサイズ可、最小化・最大化不可
                // WPF標準では「リサイズ可かつ最小化不可」の設定が存在しない(CanResizeは全部入りになる)
                // そのため、ResizeModeはCanResizeにしておき、
                // VisualTreeHelperを使ってボタンを物理的に消す。
                this.ResizeMode = ResizeMode.CanResize;

                // タイトルバー内のボタンを検索して非表示にする
                UpdateButtonVisibility(MainTitleBar, showMin: false, showMax: false, showClose: false);
            }
        }

        private Task AnimateOverlayScaleOpacity(double fromScale, double toScale, int durationMs, double? fromTranslate = null, double toTranslate = 0)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                var easing = new QuinticEase { EasingMode = EasingMode.EaseInOut };

                double currentOpacity = MonitoringOverlayContainer?.Opacity ?? 1.0;
                double targetOpacity = toScale >= 1.0 ? 1.0 : 0.95;

                // アニメ無効化時は即時反映
                if (!_animationsEnabled)
                {
                    try
                    {
                        if (MonitoringOverlayContainer != null) MonitoringOverlayContainer.Opacity = targetOpacity;
                        if (MonitoringOverlayTranslate != null) MonitoringOverlayTranslate.Y = toTranslate;
                    }
                    catch { }
                    tcs.TrySetResult(true);
                    return tcs.Task;
                }

                double startTranslate = fromTranslate ?? (MonitoringOverlayTranslate?.Y ?? 0);

                var opacityAnim = new DoubleAnimation(currentOpacity, targetOpacity, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = easing
                };
                var translateAnim = new DoubleAnimation(startTranslate, toTranslate, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = easing
                };

                opacityAnim.Completed += (s, e) =>
                {
                    try
                    {
                        if (MonitoringOverlayContainer != null) MonitoringOverlayContainer.Opacity = targetOpacity;
                        if (MonitoringOverlayTranslate != null) MonitoringOverlayTranslate.Y = toTranslate;
                    }
                    catch { }
                    tcs.TrySetResult(true);
                };

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        MonitoringOverlayContainer?.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                        MonitoringOverlayTranslate?.BeginAnimation(TranslateTransform.YProperty, translateAnim);
                    }
                    catch { tcs.TrySetResult(true); }
                });
            }
            catch
            {
                tcs.TrySetResult(true);
            }
            return tcs.Task;
        }

        // 縮小ではなく、暗転＋少し下方向へ移動する視覚効果をプリアニメとして実行（ブラーなし）
        private Task AnimateOverlayPreEffect(int durationMs)
        {
            var tcs = new TaskCompletionSource<bool>();
            try
            {
                var easing = new QuinticEase { EasingMode = EasingMode.EaseInOut };

                double currentOpacity = MonitoringOverlayContainer?.Opacity ?? 1.0;

                if (!_animationsEnabled)
                {
                    try
                    {
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Opacity = 0.94;
                            MonitoringOverlayContainer.Effect = null;
                        }
                        if (MonitoringOverlayTranslate != null)
                        {
                            MonitoringOverlayTranslate.Y = 6;
                        }
                    }
                    catch { }
                    tcs.TrySetResult(true);
                    return tcs.Task;
                }

                var opacityAnim = new DoubleAnimation(currentOpacity, 0.94, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = easing
                };
                var translateYAnim = new DoubleAnimation(0, 6, TimeSpan.FromMilliseconds(durationMs))
                {
                    EasingFunction = easing
                };

                opacityAnim.Completed += (s, e) =>
                {
                    try
                    {
                        if (MonitoringOverlayContainer != null)
                        {
                            MonitoringOverlayContainer.Opacity = 0.94;
                            MonitoringOverlayContainer.Effect = null;
                        }
                        if (MonitoringOverlayTranslate != null)
                        {
                            MonitoringOverlayTranslate.Y = 6;
                        }
                    }
                    catch { }
                    tcs.TrySetResult(true);
                };

                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        MonitoringOverlayContainer?.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                        MonitoringOverlayTranslate?.BeginAnimation(TranslateTransform.YProperty, translateYAnim);
                    }
                    catch { tcs.TrySetResult(true); }
                });
            }
            catch { tcs.TrySetResult(true); }
            return tcs.Task;
        }

        /// <summary>
        /// 制御解除（すべて元に戻す）
        /// </summary>
        private void ResetWindowControls()
        {
            this.ResizeMode = ResizeMode.CanResize;
            // ボタンを再表示
            UpdateButtonVisibility(MainTitleBar, showMin: true, showMax: true, showClose: true);
        }

        /// <summary>
        /// VisualTreeを探索してタイトルバーのボタン(PART_MinimizeButton等)を見つけ出し、Visibilityを制御する
        /// </summary>
        private void UpdateButtonVisibility(DependencyObject root, bool showMin, bool showMax, bool showClose)
        {
            if (root == null) return;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is System.Windows.Controls.Button btn)
                {
                    // Wpf.Uiのボタン名はテンプレートで決まっていることが多い
                    // PART_MinimizeButton, PART_MaximizeButton, PART_CloseButton
                    // 名前が取れない場合は、ToolTipやCommandで推測することも可能だが、まずは名前で判定

                    if (btn.Name == "PART_MinimizeButton")
                    {
                        btn.Visibility = showMin ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (btn.Name == "PART_MaximizeButton")
                    {
                        btn.Visibility = showMax ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else if (btn.Name == "PART_CloseButton")
                    {
                        btn.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;
                    }
                }

                // 再帰探索
                UpdateButtonVisibility(child, showMin, showMax, showClose);
            }
        }
    }

    public class MonitoringSession
    {
        public bool IsActive { get; set; }
        public DateTime StartTime { get; set; }
        public double DurationMinutes { get; set; }
        public int CurrentPenaltyLevel { get; set; }
        public string Goal { get; set; } = string.Empty;
        public string NgItem { get; set; } = string.Empty;
        public DateTime EndTime => StartTime.AddMinutes(DurationMinutes);
        public double RemainingSeconds
        {
            get
            {
                var remaining = (EndTime - DateTime.Now).TotalSeconds;
                return remaining > 0 ? remaining : 0;
            }
        }
    }
}