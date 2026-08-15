using System;
using System.Threading.Tasks;
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
        // 수집(GetCurrentStatus)이 아직 끝나지 않았는데 다음 틱이 도는 것을 막는 재진입 가드.
        // 백그라운드 수집이 1초를 넘겨도 PerformanceCounter/LibreHardwareMonitor가 동시에 호출되지 않도록 보장합니다.
        private bool _isTicking = false;
        private DashboardViewModel? _viewModel;

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
            // 재진입 방지: 이전 수집이 아직 끝나지 않았다면 이번 틱은 건너뜁니다.
            // (수집 안에 CPU 500ms 샘플링이 있어 1초를 넘길 수 있으므로 겹침을 막습니다.)
            if (_isTicking) return;
            _isTicking = true;

            try {
                // 무거운 수집 작업(전체 프로세스 열거 + CPU 500ms 샘플링)을 백그라운드 스레드에서 실행합니다.
                // 이렇게 하면 UI 스레드가 블로킹되지 않아 화면 버벅임이 사라집니다.
                // await 이후 코드는 다시 UI 스레드에서 실행되므로 아래 UI 갱신은 안전합니다.
                var status = await Task.Run(() => _monitor.GetCurrentStatus());

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
            } finally {
                _isTicking = false;
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
                // 1. 현재 상태(머신 ID · 프로세스 목록)를 수집해 서버에 최적화 분석을 요청
                var status = _monitor.GetCurrentStatus();
                var machineId = status.MachineId ?? Environment.MachineName;

                // killList: null = 서버 통신 실패, 빈 리스트 = 정상 응답이지만 정리 대상 없음
                var killList = await _apiClient.RequestOptimizationAsync(machineId, status.TopProcesses);

                if (killList == null) {
                    // 2-a. 서버 통신 자체가 실패한 경우에만 기존 하드코딩 방식으로 폴백
                    System.Diagnostics.Debug.WriteLine("[UPM] 최적화 분석 통신 실패 → 로컬 폴백 실행");
                    int fallbackCount = _monitor.OptimizeSystem();
                    MessageBox.Show($"서버에 연결하지 못해 로컬 규칙으로 {fallbackCount}개의 프로세스를 정리했습니다.",
                        "최적화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (killList.Count == 0) {
                    // 2-b. 정상 응답인데 종료 대상이 없으면 폴백하지 않고 그대로 알립니다.
                    MessageBox.Show("정리할 프로세스가 없습니다.", "최적화", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2-c. 서버가 내려준 killList를 규칙 기반으로 종료
                var killedNames = _monitor.OptimizeSystemByRules(killList);

                // 3. 실제 종료된 목록을 서버에 보고 (이력 저장용). 실패해도 앱은 계속 진행됩니다.
                await _apiClient.ReportKilledProcessesAsync(machineId, killedNames);

                MessageBox.Show($"{killedNames.Count}개의 불필요한 프로세스를 정리했습니다.", "최적화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            } catch (Exception ex) {
                // 통신 외 예기치 못한 오류 발생 시에도 앱이 멈추지 않도록 로컬 방식으로 폴백
                System.Diagnostics.Debug.WriteLine($"[UPM] 최적화 처리 중 예외, 폴백 실행: {ex.Message}");
                int count = _monitor.OptimizeSystem();
                MessageBox.Show($"{count}개의 불필요한 프로세스를 정리했습니다.", "최적화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
            } finally {
                BoostButton.IsEnabled = true;
                BoostButtonLabel.Text = "빠른 최적화";
                BoostButtonIcon.Visibility = Visibility.Visible;
            }
        }

        private async void WhitelistButton_Click(object sender, RoutedEventArgs e) {
            WhitelistButton.IsEnabled = false;
            try {
                // 프로세스 목록 수집(GetCurrentStatus)은 CPU 500ms 샘플링 등으로 무거우므로
                // UI 스레드 블로킹을 피하기 위해 백그라운드 스레드에서 실행합니다.
                var status = await Task.Run(() => _monitor.GetCurrentStatus());
                var machineId = status.MachineId ?? Environment.MachineName;

                var window = new WhitelistWindow(_apiClient, machineId, status.TopProcesses) {
                    Owner = this
                };
                window.ShowDialog();
            } catch (Exception ex) {
                MessageBox.Show($"화이트리스트 창을 열지 못했습니다.\n{ex.Message}", "UPM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            } finally {
                WhitelistButton.IsEnabled = true;
            }
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