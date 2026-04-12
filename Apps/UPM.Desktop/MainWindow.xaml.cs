using System;
using System.Windows;
using System.Windows.Threading;
using UPM.Core;
using UPM.Models;
using UPM.Communication;

namespace UPM.Desktop {
    public partial class MainWindow : Window {
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;
        private readonly ApiServerClient _apiClient;
        private int _tickCounter = 0;

        public MainWindow() {
            InitializeComponent();

            // 1. 수집 엔진 및 서버 클라이언트 초기화
            _monitor = new HardwareMonitor();
            _apiClient = new ApiServerClient("http://localhost:8000"); // 로컬 서버 주소로 변경

            // 2. 초기 1회 하드웨어 사양 로드 및 서버 전송
            LoadHardwareSpecs();

            // 3. 실시간 업데이트 타이머 시작
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private async void LoadHardwareSpecs() {
            try {
                var specs = _monitor.GetHardwareSpecs();
                
                // UI 업데이트
                CpuNameText.Text = $"CPU: {specs.CpuName}\n({specs.CpuCores}C / {specs.CpuThreads}T)";
                GpuNameText.Text = $"GPU: {specs.GpuName}";
                RamTotalText.Text = $"Total RAM: {specs.RamTotal}";
                MainboardText.Text = $"MB: {specs.Mainboard}";

                // 서버로 사양 전송 (현재 서버에 엔드포인트가 없으므로 주석 처리하여 404 방지)
                // await _apiClient.SendHardwareSpecsAsync(specs);
            } catch (Exception ex) {
                MessageBox.Show($"사양 로드/전송 오류: {ex.Message}");
            }
        }

        private async void Timer_Tick(object? sender, EventArgs e) {
            // 실시간 데이터 수집
            var status = _monitor.GetCurrentStatus();

            // UI 업데이트
            CpuGauge.Value = status.CpuUsagePercent;
            RamGauge.Value = status.RamUsagePercent;
            GpuGauge.Value = status.GpuTemperatureCelsius;

            // 5초마다 서버로 상태 정보 전송
            _tickCounter++;
            if (_tickCounter >= 5) {
                _tickCounter = 0;
                await _apiClient.SendSystemStatusAsync(status);
            }
        }

        private void BoostButton_Click(object sender, RoutedEventArgs e) {
            // 버튼 비활성화로 중복 클릭 방지
            BoostButton.IsEnabled = false;
            BoostButton.Content = "BOOSTING...";

            // 시스템 최적화 실행
            int count = _monitor.OptimizeSystem();

            MessageBox.Show($"{count}개의 불필요한 프로세스를 정리했습니다!", "UPM Boost 완료");

            // 버튼 복구
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