using System;

namespace UPM.Models {
    /// <summary>
    /// PC의 실시간 성능 상태 정보를 담는 모델 클래스입니다.
    /// </summary>
    public class SystemStatusModel {
        // 1. CPU 정보
        public double CpuUsage { get; set; }           // CPU 사용량 (%)
        public string? CpuStatus { get; set; }         // CPU 상태 메시지

        // 2. RAM 정보
        public double RamUsage { get; set; }           // RAM 점유율 (%)
        public string? RamStatus { get; set; }         // RAM 상태 메시지

        // 3. GPU 정보
        public double GpuTemperature { get; set; }     // GPU 온도 (°C)

        // 4. 디스크 정보
        public double DiskUsage { get; set; }          // 디스크 사용량 (%)
        public string? DiskStatus { get; set; }        // 디스크 상태 메시지

        // 5. 메타 데이터
        public DateTime Timestamp { get; set; }        // 데이터 수집 시간
        public string? PcName { get; set; }            // PC 식별 이름
    }
}