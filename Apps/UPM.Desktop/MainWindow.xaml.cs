using System;
using System.Windows;
using System.Windows.Threading;
using UPM.Core;
using UPM.Communication;
using UPM.Models;
using UPM.Communication;
using UPM.Desktop.ViewModels;

namespace UPM.Desktop {
    public partial class MainWindow : Window {
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;
        private readonly ApiServerClient _apiClient;
        private int _tickCounter = 0;
        private DashboardViewModel? _viewModel;

        private readonly UpmApiService _api = new UpmApiService();

        public MainWindow() {
            InitializeComponent();
            ApplyProcessListVisibility(true);

            // ViewModel 참조 가져오기
            _viewModel = DataContext as DashboardViewModel;

            // 1. 수집 엔진 및 서버 클라이언트 초기화
            _monitor = new HardwareMonitor();
            _apiClient = new ApiServerClient("http://localhost:8000");

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
                MessageBox.Show($"사양 로드 오류: {ex.Message}");
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e) {
            // 실시간 데이터 수집
            var status = _monitor.GetCurrentStatus();

            // ViewModel 업데이트 (게이지 수치 반영)
            if (_viewModel != null) {
                var cpu = status.CpuUsagePercent;
                _viewModel.CpuValue.Value = cpu;
                _viewModel.CpuTrackValue.Value = 100 - cpu;

                var ram = status.RamUsagePercent;
                _viewModel.RamValue.Value = ram;
                _viewModel.RamTrackValue.Value = 100 - ram;

                var gpuC = status.GpuTemperatureCelsius;
                var gpuArc = Math.Clamp(gpuC, 0, 100);
                _viewModel.GpuValue.Value = gpuArc;
                _viewModel.GpuTrackValue.Value = 100 - gpuArc;
                _viewModel.GpuTemperatureCelsius.Value = gpuC;
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

        private void ProcessListToggle_OnChecked(object sender, RoutedEventArgs e) {
            ApplyProcessListVisibility(true);
        }

        private void ProcessListToggle_OnUnchecked(object sender, RoutedEventArgs e) {
            ApplyProcessListVisibility(false);
        }

        private void ApplyProcessListVisibility(bool visible) {
            // InitializeComponent 중 IsChecked로 Checked가 뜰 때 자식 필드가 아직 null일 수 있음
            if (ProcessDetailsHost == null || ProcessListToggle == null)
                return;
            ProcessDetailsHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            ProcessListToggle.Content = visible ? "프로세스 숨기기" : "프로세스 표시";
            if (IsLoaded)
                RefreshWindowHeightToContent();
        }

        private void RefreshWindowHeightToContent() {
            Dispatcher.BeginInvoke(new Action(() => {
                SizeToContent = SizeToContent.Manual;
                SizeToContent = SizeToContent.Height;
            }), DispatcherPriority.Background);
        }

        private void BoostButton_Click(object sender, RoutedEventArgs e) {
            BoostButton.IsEnabled = false;
            BoostButton.Content = "BOOSTING...";

            int count = _monitor.OptimizeSystem();

            MessageBox.Show($"{count}개의 불필요한 프로세스를 정리했습니다!", "UPM Boost 완료");

            BoostButton.IsEnabled = true;
            BoostButton.Content = "QUICK BOOST";
        }

        protected override void OnClosed(EventArgs e) {
            _timer.Stop();
            _monitor.Dispose();
            base.OnClosed(e);
        }
    }
}