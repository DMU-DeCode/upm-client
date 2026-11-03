using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UPM.Core;
using UPM.Communication;
using UPM.Desktop.ViewModels;
using UPM.Models;

namespace UPM.Desktop
{
    public partial class MainWindow : Window
    {
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;
        private readonly ApiServerClient _apiClient;
        private readonly MobileControlHttpServer _controlServer;
        private readonly string _machineId = Environment.MachineName;
        private int _tickCounter = 0;
        private bool _isTicking = false;
        private DashboardViewModel? _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            ApplyHardwareDetailVisibility(DetailExpandToggle.IsChecked == true);

            // ViewModel 참조 가져오기
            _viewModel = DataContext as DashboardViewModel;

            // 테마: 현재 테마 반영 + 변경 구독
            ApplyThemeVisuals();
            ThemeManager.ThemeChanged += OnThemeChanged;

            // 1. 수집 엔진 및 서버 클라이언트 초기화
            _monitor = new HardwareMonitor();
            _apiClient = new ApiServerClient(UpmAppSettings.MetricsServerBaseUrl);

            _controlServer = new MobileControlHttpServer(_monitor);
            try
            {
                _controlServer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPM] 모바일 제어 서버를 시작하지 못했습니다: {ex.Message}");
            }

            // 2. 초기 1회 하드웨어 사양 로드 및 서버 예외 목록 수신
            _ = LoadHardwareSpecsAsync();

            // 로컬 화이트리스트 즉시 반영 (서버 응답 전 최적화 실행 시 보호 보장)
            var localWhitelist = LocalWhitelistStore.Load();
            if (localWhitelist.Count > 0)
                _monitor.SetUserExceptions(localWhitelist);

            _ = LoadServerExceptionsAsync();

            // 3. 실시간 업데이트 타이머 시작
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private async Task LoadServerExceptionsAsync()
        {
            try
            {
                var exceptions = await _apiClient.GetUserExceptionsAsync(_machineId);
                if (exceptions != null && exceptions.Count > 0)
                {
                    _monitor.SetUserExceptions(exceptions);
                    // 서버 성공 시 로컬에도 동기화
                    LocalWhitelistStore.Save(exceptions);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPM] 서버 예외 목록 로드 실패, 로컬 폴백: {ex.Message}");
                // 서버 연결 실패 시 로컬 화이트리스트에서 로드
                var localList = LocalWhitelistStore.Load();
                if (localList.Count > 0)
                {
                    _monitor.SetUserExceptions(localList);
                }
            }
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshWindowHeightToContent();
        }

        private async Task LoadHardwareSpecsAsync()
        {
            try
            {
                var specs = await Task.Run(() => _monitor.GetHardwareSpecs());

                // UI 업데이트 (이름이 부여된 TextBlock 제어)
                CpuNameText.Text = specs.CpuName;
                GpuNameText.Text = specs.GpuName;
                RamTotalText.Text = specs.RamTotal;
                MainboardText.Text = specs.Mainboard;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"사양을 불러오지 못했습니다.\n{ex.Message}", "UPM");
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isTicking) return;
            _isTicking = true;

            try
            {
                // 무거운 프로세스/센서 수집은 백그라운드 스레드에서 실행하여 UI 프리징 방지
                var status = await Task.Run(() => _monitor.GetCurrentStatus());
                status.MachineId = _machineId;

                // ViewModel 업데이트 (게이지 수치 · 호 방향 색 조각)
                if (_viewModel != null)
                {
                    var cpu = status.CpuUsagePercent;
                    var ram = status.RamUsagePercent;
                    var gpuUsage = status.GpuUsagePercent;
                    var gpuC = status.GpuTemperatureCelsius;

                    _viewModel.ApplyCpuGauge(cpu);
                    _viewModel.ApplyRamGauge(ram);
                    _viewModel.ApplyGpuGauge(gpuUsage, gpuC);

                    ApplyGaugeHeatDots(cpu, ram, gpuUsage);
                }

                TopProcessItems.ItemsSource = status.TopProcesses;

                // 5초마다 서버로 상태 정보 전송
                _tickCounter++;
                if (_tickCounter >= 5)
                {
                    _tickCounter = 0;
                    _ = _apiClient.SendSystemStatusAsync(status);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPM] 타이머 수집 오류: {ex.Message}");
            }
            finally
            {
                _isTicking = false;
            }
        }

        private void DetailExpandToggle_OnChecked(object sender, RoutedEventArgs e)
        {
            ApplyHardwareDetailVisibility(true);
        }

        private void DetailExpandToggle_OnUnchecked(object sender, RoutedEventArgs e)
        {
            ApplyHardwareDetailVisibility(false);
        }

        private void ProcessScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv)
                return;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void ApplyHardwareDetailVisibility(bool expanded)
        {
            if (HardwareDetailHost == null || DetailExpandToggle == null)
                return;
            HardwareDetailHost.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            DetailExpandToggle.Content = expanded ? "간단히 보기" : "자세히 보기";
            if (IsLoaded)
                RefreshWindowHeightToContent();
        }

        private void ApplyGaugeHeatDots(double cpuPercent, double ramPercent, double gpuHeatPercent)
        {
            CpuHeatDot.Fill = HeatBrush(cpuPercent);
            RamHeatDot.Fill = HeatBrush(ramPercent);
            GpuHeatDot.Fill = HeatBrush(gpuHeatPercent);
        }

        private static SolidColorBrush HeatBrush(double percent)
        {
            var c = DashboardViewModel.UsageHeatColor(percent);
            return new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
        }

        private void RefreshWindowHeightToContent()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            }), DispatcherPriority.Background);
        }

        private void AutoCloseModeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool isAuto = AutoCloseModeToggle.IsChecked == true;
            _monitor.AutoCloseMode = isAuto;
            AutoCloseModeLabel.Text = isAuto ? "자동 종료 모드" : "선택 종료 모드";
            AutoCloseModeIcon.Text = isAuto ? "\uE945" : "\uE762";
            if (ProcessCheckColumnHeader != null)
                ProcessCheckColumnHeader.Text = isAuto ? "보호" : "선택";
        }

        private async void ProcessProtection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ProcessInfoModel proc)
            {
                bool isProtected = _monitor.ToggleProcessProtection(proc.ProcessName);
                proc.IsProtected = isProtected;
                if (isProtected)
                    proc.IsSelectedForKill = false;

                // 서버 DB 동기화
                string action = isProtected ? "add" : "remove";
                await _apiClient.UpdateUserExceptionAsync(_machineId, proc.ProcessName, action);
            }
        }

        private void ProcessKillSelection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is ProcessInfoModel proc)
            {
                bool selected = cb.IsChecked == true;
                _monitor.SetProcessSelectedForKill(proc.ProcessName, selected);
                proc.IsSelectedForKill = selected;
            }
        }

        private async void KillProcessItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessInfoModel proc)
            {
                if (proc.IsSystemCritical)
                {
                    MessageBox.Show("시스템 필수 프로세스는 종료할 수 없습니다.", "보호됨", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string promptText = proc.ProcessCount > 1
                    ? $"프로세스 '{proc.ProcessName}'의 모든 실행 인스턴스({proc.ProcessCount}개)를 종료하시겠습니까?"
                    : $"프로세스 '{proc.ProcessName}' (PID: {proc.ProcessId})를 종료하시겠습니까?";

                var confirm = MessageBox.Show(promptText, "프로세스 종료 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (confirm == MessageBoxResult.Yes)
                {
                    if (proc.ProcessCount > 1)
                    {
                        int killedCount = _monitor.KillProcessGroupByName(proc.ProcessName, out var killedList);
                        if (killedCount > 0)
                        {
                            await _apiClient.ReportOptimizationAsync(_machineId, killedList);
                            MessageBox.Show($"프로세스 '{proc.ProcessName}' ({killedCount}개 인스턴스)가 성공적으로 종료되었습니다.", "종료 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("프로세스를 종료하지 못했습니다. (이미 종료되었거나 권한이 필요함)", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        if (_monitor.KillProcessById(proc.ProcessId, out string killedName))
                        {
                            await _apiClient.ReportOptimizationAsync(_machineId, new List<string> { killedName });
                            MessageBox.Show($"프로세스 '{killedName}'가 성공적으로 종료되었습니다.", "종료 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("프로세스를 종료하지 못했습니다. (이미 종료되었거나 권한이 필요함)", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        private async void BoostButton_Click(object sender, RoutedEventArgs e)
        {
            BoostButton.IsEnabled = false;
            BoostButtonLabel.Text = "최적화 중…";
            BoostButtonIcon.Visibility = Visibility.Collapsed;

            var currentProcesses = TopProcessItems.ItemsSource as List<ProcessInfoModel>;
            // 백그라운드 스레드에서 실행하여 UI 스레드 블로킹 및 앱 크래시 방지
            int count = await Task.Run(async () =>
                await _monitor.OptimizeSystemAsync(_apiClient, _machineId, currentProcesses));

            MessageBox.Show($"{count}개의 프로세스를 정리했습니다.", "최적화 완료", MessageBoxButton.OK, MessageBoxImage.Information);

            BoostButton.IsEnabled = true;
            BoostButtonLabel.Text = "빠른 최적화";
            BoostButtonIcon.Visibility = Visibility.Visible;
        }

        private async void WhitelistButton_Click(object sender, RoutedEventArgs e)
        {
            WhitelistButton.IsEnabled = false;
            try
            {
                // 화이트리스트에는 상위 8개가 아닌 현재 실행 중인 모든 프로세스를 백그라운드에서 수집하여 전달
                var allProcesses = await Task.Run(() => _monitor.GetAllRunningProcesses());
                // _monitor를 전달하여 화이트리스트 변경 시 인메모리 예외 목록도 즉시 갱신
                var whitelistWindow = new WhitelistWindow(_apiClient, _machineId, allProcesses, _monitor)
                {
                    Owner = this
                };
                whitelistWindow.ShowDialog();
            }
            finally
            {
                WhitelistButton.IsEnabled = true;
            }
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.Toggle();
        }

        private void OnThemeChanged(object? sender, EventArgs e) => ApplyThemeVisuals();

        /// <summary>XAML 동적 리소스로 처리되지 않는 부분(테마 버튼 아이콘, LiveCharts 트랙 색, 테마별 로고) 동기화.</summary>
        private void ApplyThemeVisuals()
        {
            bool isDark = ThemeManager.Current == AppTheme.Dark;
            // 다크일 때는 해(→라이트로), 라이트일 때는 달(→다크로)
            ThemeToggleIcon.Text = isDark ? "\uE706" : "\uE708";
            ThemeToggleButton.ToolTip = isDark ? "라이트 테마로 전환" : "다크 테마로 전환";

            if (AppLogoImage != null)
            {
                var uri = isDark
                    ? new Uri("pack://application:,,,/Assets/upm_logo_dark.gif", UriKind.Absolute)
                    : new Uri("pack://application:,,,/Assets/upm_logo_light.png", UriKind.Absolute);
                var bitmap = new BitmapImage(uri);
                WpfAnimatedGif.ImageBehavior.SetAnimatedSource(AppLogoImage, bitmap);
            }

            var c = ThemeManager.GetColor("Color.ArcTrack", Color.FromRgb(76, 78, 86));
            _viewModel?.ApplyTrackColor(new SkiaSharp.SKColor(c.R, c.G, c.B, c.A));
        }

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.ThemeChanged -= OnThemeChanged;
            _timer.Stop();
            _controlServer.Dispose();
            _apiClient.Dispose();
            _monitor.Dispose();
            base.OnClosed(e);
        }
    }
}
