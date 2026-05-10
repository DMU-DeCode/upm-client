using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UPM.Models {
    /// <summary>
    /// 서버의 ProcessInfo Pydantic 모델과 일치하는 클래스입니다.
    /// </summary>
    public class ProcessInfoModel {
        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = "";

        [JsonPropertyName("memoryWorkingSetBytes")]
        public long MemoryWorkingSetBytes { get; set; }

        /// <summary>현재 사용 중인 RAM(설치량 − 여유) 대비 이 프로세스 작업 집합 비율(%).</summary>
        [JsonPropertyName("memoryPercentOfUsedRam")]
        public double MemoryPercentOfUsedRam { get; set; }

        /// <summary>아이콘 추출용 실행 파일 경로 — API 전송 제외.</summary>
        [JsonIgnore]
        public string? ExecutablePath { get; set; }

        [JsonPropertyName("isSystemCritical")]
        public bool IsSystemCritical { get; set; }
    }

    /// <summary>
    /// v3.0 API 규격에 100% 맞춘 시스템 성능 상태 모델입니다.
    /// </summary>
    public class SystemStatusModel {
        [JsonPropertyName("machineId")]
        public string? MachineId { get; set; }

        [JsonPropertyName("cpuUsagePercent")]
        public double CpuUsagePercent { get; set; }

        [JsonPropertyName("ramUsagePercent")]
        public double RamUsagePercent { get; set; }

        [JsonPropertyName("gpuUsagePercent")]
        public double GpuUsagePercent { get; set; }

        [JsonPropertyName("gpuTemperatureCelsius")]
        public double GpuTemperatureCelsius { get; set; }

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("topProcesses")]
        public List<ProcessInfoModel> TopProcesses { get; set; } = new();
    }
}
