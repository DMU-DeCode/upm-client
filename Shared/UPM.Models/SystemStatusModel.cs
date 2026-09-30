using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UPM.Models
{
    /// <summary>
    /// 서버의 ProcessInfo Pydantic 모델과 일치하는 클래스입니다.
    /// </summary>
    public class ProcessInfoModel : INotifyPropertyChanged
    {
        private bool _isProtected;
        private bool _isSelectedForKill;
        private bool _hasVisibleWindow;
        private double _cpuPercent;

        private int _processCount = 1;

        [JsonPropertyName("processId")]
        public int ProcessId { get; set; }

        [JsonPropertyName("processName")]
        public string ProcessName { get; set; } = "";

        [JsonIgnore]
        public int ProcessCount
        {
            get => _processCount;
            set
            {
                if (_processCount != value)
                {
                    _processCount = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        [JsonIgnore]
        public string DisplayName => ProcessCount > 1 ? $"{ProcessName} ({ProcessCount})" : ProcessName;

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

        [JsonPropertyName("hasVisibleWindow")]
        public bool HasVisibleWindow
        {
            get => _hasVisibleWindow;
            set
            {
                if (_hasVisibleWindow != value)
                {
                    _hasVisibleWindow = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("cpuPercent")]
        public double CpuPercent
        {
            get => _cpuPercent;
            set
            {
                if (Math.Abs(_cpuPercent - value) > 0.001)
                {
                    _cpuPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("isCurrentUserOwned")]
        public bool IsCurrentUserOwned { get; set; }

        /// <summary>사용자가 종료 예외(보호)로 설정했는지 여부 (UI 연동)</summary>
        [JsonIgnore]
        public bool IsProtected
        {
            get => _isProtected;
            set
            {
                if (_isProtected != value)
                {
                    _isProtected = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>사용자가 선택 종료 대상으로 체크했는지 여부 (UI 연동)</summary>
        [JsonIgnore]
        public bool IsSelectedForKill
        {
            get => _isSelectedForKill;
            set
            {
                if (_isSelectedForKill != value)
                {
                    _isSelectedForKill = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// v3.0 API 규격에 100% 맞춘 시스템 성능 상태 모델입니다.
    /// </summary>
    public class SystemStatusModel
    {
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
