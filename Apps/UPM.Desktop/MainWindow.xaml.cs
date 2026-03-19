using System;
using System.Windows;
using System.Windows.Threading;
using UPM.Core;
using UPM.Models;

namespace UPM.Desktop {
    public partial class MainWindow : Window {
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;

        public MainWindow() {
            InitializeComponent();

            // 1. 수집 엔진 초기화
            _monitor = new HardwareMonitor();

            // 2. 초기 1회 하드웨어 사양 로드
            LoadHardwareSpecs();

            // 3. 실시간 업데이트 타이머 시작
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void LoadHardwareSpecs() {
            try {
                var specs = _monitor.GetHardwareSpecs();
                CpuNameText.Text = $"CPU: {specs.CpuName}\n({specs.CpuCores}C / {specs.CpuThreads}T)";
                GpuNameText.Text = $"GPU: {specs.GpuName}";
                RamTotalText.Text = $"Total RAM: {specs.RamTotal}";
                MainboardText.Text = $"MB: {specs.Mainboard}";
            } catch (Exception ex) {
                MessageBox.Show($"사양 로드 오류: {ex.Message}");
            }
        }

        private void Timer_Tick(object? sender, EventArgs e) {
            // 실시간 데이터 수집
            var status = _monitor.GetCurrentStatus();

            // 게이지 바늘 업데이트
            CpuGauge.Value = status.CpuUsage;
            RamGauge.Value = status.RamUsage;
            GpuGauge.Value = status.GpuTemperature;
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