using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop; // 必須
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using MonitAI.Core;           // 必須

namespace MonitAI.UI.Features.Main
{
    public partial class MainWindow : FluentWindow
    {
        private double _restoredWidth;
        private double _restoredHeight;
        private double _restoredTop;
        private double _restoredLeft;
        private WindowState _restoredState;
        private ResizeMode _restoredResizeMode;
        private WindowStyle _restoredWindowStyle;

        private MonitAI.UI.Features.Setup.SetupPage? _setupPage;
        private MonitAI.UI.Features.Settings.SettingsPage? _settingsPage;
        private MonitAI.UI.Features.MonitoringOverlay.MonitoringOverlay? _monitoringOverlay;

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
                    // 3. 通常モード: 最小化(SC_MINIMIZE)をブロック、リサイズは許可
                    if (command == NativeMethods.SC_MINIMIZE)
                    {
                        handled = true;
                        return IntPtr.Zero;
                    }
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

            RootNavigation.Visibility = Visibility.Collapsed;
            MonitoringOverlayContainer.Visibility = Visibility.Visible;

            _isMonitoring = true;

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

            if (_monitoringOverlay?.IsMiniMode == true)
            {
                RestoreWindow();
                _monitoringOverlay?.ToggleMode();
            }
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
        private void OnToggleMiniModeClick(object? sender, EventArgs e)
        {
            if (_monitoringOverlay == null) return;

            _monitoringOverlay.ToggleMode();

            if (_monitoringOverlay.IsMiniMode)
            {
                // === ミニモードへ遷移 ===
                SaveWindowState();

                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;

                WindowStyle = WindowStyle.None;

                // 【重要】余白を消すための設定
                ExtendsContentIntoTitleBar = false;

                ResizeMode = ResizeMode.NoResize;
                Topmost = true;
                ShowInTaskbar = true;

                // 最小サイズ制限を解除
                MinWidth = 0;
                MinHeight = 0;

                // サイズを180x180に固定 (layoutsample準拠)
                var dpiScale = GetDpiScale();
                double miniWidth = 180;
                double miniHeight = 180;

                Width = miniWidth;
                Height = miniHeight;
                MaxWidth = miniWidth;
                MaxHeight = miniHeight;

                // 背景を透明に
                Background = Brushes.Transparent;

                // 画面右下に配置
                var area = SystemParameters.WorkArea;
                Left = area.Right - miniWidth - (20 / dpiScale.X);
                Top = area.Bottom - miniHeight - (20 / dpiScale.Y);

                MainTitleBar.Visibility = Visibility.Collapsed;

                ApplyWindowMode(isMini: true);
            }
            else
            {
                // === 通常モードへ復帰 ===
                RestoreWindow();

                // タイトルバー設定を戻す
                ExtendsContentIntoTitleBar = true;

                MaxWidth = double.PositiveInfinity;
                MaxHeight = double.PositiveInfinity;

                Background = (Brush)FindResource("ApplicationBackgroundBrush");

                MainTitleBar.Visibility = Visibility.Visible;

                ApplyWindowMode(isMini: false);
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
            _restoredWidth = Width;
            _restoredHeight = Height;
            _restoredTop = Top;
            _restoredLeft = Left;
            _restoredState = WindowState;
            _restoredResizeMode = ResizeMode;
            _restoredWindowStyle = WindowStyle;
        }

        private void RestoreWindow()
        {
            WindowStyle = _restoredWindowStyle;
            ResizeMode = _restoredResizeMode;
            ExtendsContentIntoTitleBar = true;
            Topmost = false;
            Width = _restoredWidth;
            Height = _restoredHeight;
            Top = _restoredTop;
            Left = _restoredLeft;
            WindowState = _restoredState;
            Background = (Brush)FindResource("ApplicationBackgroundBrush");
            MainTitleBar.Visibility = Visibility.Visible;
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