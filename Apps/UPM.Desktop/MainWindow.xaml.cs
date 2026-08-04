using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UPM.Core;
using UPM.Communication;
using UPM.Desktop.ViewModels;

namespace UPM.Desktop {
    public partial class MainWindow : Window {
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;
        private readonly ApiServerClient _apiClient;
        private readonly MobileControlHttpServer _controlServer;
        private int _tickCounter = 0;
        private DashboardViewModel? _viewModel;
        private readonly List<string> _userWhiteList = new();

        public MainWindow() {
            InitializeComponent();
            ApplyHardwareDetailVisibility(DetailExpandToggle.IsChecked == true);

            // ViewModel 참조 가져오기
            _viewModel = DataContext as DashboardViewModel;

            // 1. 수집 엔진 및 서버 클라이언트 초기화
            _monitor = new HardwareMonitor();
            _apiClient = new ApiServerClient(UpmAppSettings.MetricsServerBaseUrl);

            _controlServer = new MobileControlHttpServer(_monitor);
            try {
                _controlServer.Start();
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[UPM] 모바일 제어 서버를 시작하지 못했습니다: {ex.Message}");
            }

            // 2. 초기 1회 하드웨어 사양 로드 및 서버 전송
            LoadHardwareSpecs();

            // 3. 실시간 업데이트 타이머 시작
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) {
            RefreshWindowHeightToContent();
            LoadUserExceptions();
        }

        private void LoadHardwareSpecs() {
            try {
                var specs = _monitor.GetHardwareSpecs();
                
                // UI 업데이트 (이름이 부여된 TextBlock 제어)
                CpuNameText.Text = specs.CpuName;
                GpuNameText.Text = specs.GpuName;
                RamTotalText.Text = specs.RamTotal;
                MainboardText.Text = specs.Mainboard;

                // 서버로 사양 전송 (필요시 활성화)
                // _apiClient.SendHardwareSpecsAsync(specs);
            } catch (Exception ex) {
                MessageBox.Show($"사양을 불러오지 못했습니다.\n{ex.Message}", "UPM");
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e) {
            // 실시간 데이터 수집
            var status = _monitor.GetCurrentStatus();

            // ViewModel 업데이트 (게이지 수치 · 호 방향 색 조각)
            if (_viewModel != null) {
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
            if (_tickCounter >= 5) {
                _tickCounter = 0;
                try {
                    await _apiClient.SendSystemStatusAsync(status);
                } catch {
                    // 서버 연결 실패 시 무시
                }
            }
        }

        private void DetailExpandToggle_OnChecked(object sender, RoutedEventArgs e) {
            ApplyHardwareDetailVisibility(true);
        }

        private void DetailExpandToggle_OnUnchecked(object sender, RoutedEventArgs e) {
            ApplyHardwareDetailVisibility(false);
        }

        private void ProcessScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e) {
            if (sender is not ScrollViewer sv)
                return;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        private void ApplyHardwareDetailVisibility(bool expanded) {
            if (HardwareDetailHost == null || DetailExpandToggle == null)
                return;
            HardwareDetailHost.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            DetailExpandToggle.Content = expanded ? "간단히 보기" : "자세히 보기";
            if (IsLoaded)
                RefreshWindowHeightToContent();
        }

        private void ApplyGaugeHeatDots(double cpuPercent, double ramPercent, double gpuHeatPercent) {
            CpuHeatDot.Fill = HeatBrush(cpuPercent);
            RamHeatDot.Fill = HeatBrush(ramPercent);
            GpuHeatDot.Fill = HeatBrush(gpuHeatPercent);
        }

        private static SolidColorBrush HeatBrush(double percent) {
            var c = DashboardViewModel.UsageHeatColor(percent);
            return new SolidColorBrush(Color.FromRgb(c.Red, c.Green, c.Blue));
        }

        private void RefreshWindowHeightToContent() {
            Dispatcher.BeginInvoke(new Action(() => {
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            }), DispatcherPriority.Background);
        }

        private async void BoostButton_Click(object sender, RoutedEventArgs e) {
            BoostButton.IsEnabled = false;
            BoostButtonLabel.Text = "최적화 중…";
            BoostButtonIcon.Visibility = Visibility.Collapsed;

            try {
                // 서버에서 블랙리스트 가져오기
                var rules = await _apiClient.GetOptimizationRulesAsync();
                var blackList = rules?.BlackList ?? new List<string>();

                // 화이트리스트 제외 후 정리
                int count = _monitor.OptimizeSystem(blackList, _userWhiteList);
                MessageBox.Show($"{count}개의 불필요한 프로세스를 정리했습니다.", 
                                "최적화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch {
                MessageBox.Show("최적화 중 오류가 발생했습니다.", "UPM");
            } finally {
                BoostButton.IsEnabled = true;
                BoostButtonLabel.Text = "빠른 최적화";
                BoostButtonIcon.Visibility = Visibility.Visible;
            }
        }

        // 화이트리스트 추가 버튼
        private async void WhitelistAddButton_Click(object sender, RoutedEventArgs e) {
            var name = WhitelistInputBox.Text.Trim();
            if (string.IsNullOrEmpty(name)) return;

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];

            if (_userWhiteList.Contains(name, StringComparer.OrdinalIgnoreCase)) {
                MessageBox.Show("이미 추가된 프로세스입니다.", "UPM");
                return;
            }

            _userWhiteList.Add(name);
            WhitelistItems.ItemsSource = null;
            WhitelistItems.ItemsSource = _userWhiteList;
            WhitelistInputBox.Text = "";

            try {
                await _apiClient.UpdateExceptionAsync(Environment.MachineName, name, "add");
            } catch { }
        }

        // 엔터키로도 추가 가능
        private void WhitelistInputBox_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter)
                WhitelistAddButton_Click(sender, e);
        }

        // 화이트리스트 삭제 버튼
        private async void WhitelistRemoveButton_Click(object sender, RoutedEventArgs e) {
            if (sender is Button btn && btn.Tag is string name) {
                _userWhiteList.Remove(name);
                WhitelistItems.ItemsSource = null;
                WhitelistItems.ItemsSource = _userWhiteList;

                try {
                    await _apiClient.UpdateExceptionAsync(Environment.MachineName, name, "remove");
                } catch { }
            }
        }

        // 앱 시작 시 서버에서 기존 화이트리스트 불러오기
        private async void LoadUserExceptions() {
            try {
                var exceptions = await _apiClient.GetExceptionsAsync(Environment.MachineName);
                _userWhiteList.AddRange(exceptions);
                WhitelistItems.ItemsSource = null;
                WhitelistItems.ItemsSource = _userWhiteList;
            } catch { }
        }
        
        protected override void OnClosed(EventArgs e) {
            _timer.Stop();
            _controlServer.Dispose();
            _apiClient.Dispose();
            _monitor.Dispose();
            base.OnClosed(e);
        }
    }
}
