using System;
using System.Windows;
using System.Windows.Threading;
using UPM.Core;
using UPM.Models;

namespace UPM.Desktop {
    public partial class MainWindow : Window {
        // 필드 선언 (nullable 설정을 통해 CS8618 경고 방지)
        private readonly HardwareMonitor _monitor;
        private readonly DispatcherTimer _timer;

        public MainWindow() {
            InitializeComponent();

            // 1. 하드웨어 수집 엔진 초기화
            _monitor = new HardwareMonitor();

            // 2. 하드웨어 사양(Specs) 정보 가져오기
            LoadHardwareSpecs();

            // 3. 실시간 모니터링 타이머 설정 (1초 주기)
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void LoadHardwareSpecs() {
            try {
                var specs = _monitor.GetHardwareSpecs();
                CpuNameText.Text = $"CPU: {specs.CpuName} ({specs.CpuCores}C/{specs.CpuThreads}T)";
                GpuNameText.Text = $"GPU: {specs.GpuName}";
                RamTotalText.Text = $"RAM: {specs.RamTotal}";
                MainboardText.Text = $"MB: {specs.Mainboard}";
            } catch (Exception ex) {
                MessageBox.Show($"사양 정보를 가져오는 중 오류 발생: {ex.Message}");
            }
        }

        private void Timer_Tick(object? sender, EventArgs e) {
            // 실시간 데이터 수집
            var status = _monitor.GetCurrentStatus();

            // UI 업데이트 (숫자 및 텍스트) [cite: 177-184]
            CpuUsageText.Text = $"CPU Usage: {status.CpuUsage}%";

            // 추가적인 UI 요소(RAM, GPU 온도 등)가 XAML에 있다면 여기서 업데이트
            // 예: GpuTempText.Text = $"{status.GpuTemperature}°C";
        }

        // 프로그램 종료 시 리소스 해제
        protected override void OnClosed(EventArgs e) {
            _timer.Stop();
            _monitor.Dispose();
            base.OnClosed(e);
        }
    }
}