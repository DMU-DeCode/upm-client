using System;
using System.Windows;
using System.Windows.Threading; // 타이머 사용
using UPM.Communication; // ApiServerClient 사용
using UPM.Models; // SystemStatusModel 사용

namespace UPM.Core
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private DispatcherTimer _monitoringTimer;
        private ApiServerClient _apiClient;

        public MainWindow()
        {
            InitializeComponent();

            // 1. ApiClient 초기화 (여기에 파이썬 서버의 ngrok 주소를 입력하세요!)
            string ngrokUrl = "https://a21b-121-133-149-115.ngrok-free.app";
            _apiClient = new ApiServerClient(ngrokUrl);

            // 2. 1초마다 실행될 타이머 설정
            _monitoringTimer = new DispatcherTimer();
            _monitoringTimer.Interval = TimeSpan.FromSeconds(1);
            _monitoringTimer.Tick += MonitoringTimer_Tick;

            // 3. 폼 실행 시 즉시 타이머 시작
            _monitoringTimer.Start();
        }

        // 1초마다 반복 실행되는 로직
        private async void MonitoringTimer_Tick(object? sender, EventArgs e)
        {
            // [Todo] 나중에 실제 하드웨어 센서 데이터를 읽어오는 로직으로 교체해야 합니다.
            // 현재는 테스트를 위해 임의의 가짜 데이터를 사용합니다.
            var currentStatus = new SystemStatusModel
            {
                MachineId = "UPM-PC-01",
                CpuUsagePercent = 45.5,
                RamUsagePercent = 55.2,
                GpuTemperatureCelsius = 62.0,
                Timestamp = DateTime.UtcNow // 현재 시간(UTC 기준) 전송
                // TopProcesses는 SystemStatusModel에서 자동으로 빈 리스트(new())로 할당됨
            };

            // 서버로 데이터 비동기 전송
            bool isSuccess = await _apiClient.SendSystemStatusAsync(currentStatus);

            // 전송 결과 디버깅 (Visual Studio 출력창에서 확인 가능)
            if (isSuccess)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] 데이터 전송 성공!");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now}] 데이터 전송 실패...");
            }
        }
    }
}