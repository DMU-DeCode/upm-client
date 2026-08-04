using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using UPM.Models;
using LibreHardwareMonitor.Hardware;

namespace UPM.Core {
    public class HardwareMonitor : IDisposable {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _availableRamCounter;
        private readonly Computer _computer;
        private double _totalRamMBytes;

        public HardwareMonitor() {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();

            _availableRamCounter = new PerformanceCounter("Memory", "Available MBytes");
            _availableRamCounter.NextValue();

            _computer = new Computer { IsGpuEnabled = true };
            _computer.Open();
        }

        // CPU-Z처럼 상세 사양 정보를 가져오는 메서드
        public HardwareSpecModel GetHardwareSpecs() {
            var specs = new HardwareSpecModel();

            try {
                // 1. CPU 정보 (이름, 코어, 스레드)
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor")) {
                    foreach (var obj in searcher.Get()) {
                        specs.CpuName = obj["Name"]?.ToString() ?? "Unknown CPU";
                        specs.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                        specs.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    }
                }

                // 2. RAM 총량
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem")) {
                    foreach (var obj in searcher.Get()) {
                        double kb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        specs.RamTotal = $"{Math.Round(kb / (1024 * 1024), 0)} GB";
                        _totalRamMBytes = kb / 1024;
                    }
                }

                // 3. GPU 이름
                foreach (var hardware in _computer.Hardware) {
                    if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd) {
                        specs.GpuName = hardware.Name;
                    }
                }

                // 4. 메인보드 정보
                using (var searcher = new ManagementObjectSearcher("SELECT Product, Manufacturer FROM Win32_BaseBoard")) {
                    foreach (var obj in searcher.Get()) {
                        specs.Mainboard = $"{obj["Manufacturer"]} {obj["Product"]}";
                    }
                }
            } catch { /* 예외 처리 */ }

            return specs;
        }

        public SystemStatusModel GetCurrentStatus() {
            var status = new SystemStatusModel { 
                Timestamp = DateTime.Now, 
                MachineId = Environment.MachineName 
            };
            
            status.CpuUsagePercent = Math.Round(_cpuCounter.NextValue(), 1);

            // 물리 메모리 중 현재 사용 중인 양(MB) = 설치량(MB) − 여유(MB). 프로세스 막대 분모로 동일 값 사용.
            double availableRamMb = _availableRamCounter.NextValue();
            double usedRamMBytes = Math.Max(0, _totalRamMBytes - availableRamMb);
            status.RamUsagePercent = _totalRamMBytes > 0
                ? Math.Round(usedRamMBytes / _totalRamMBytes * 100, 1)
                : 0;

            var usedRamBytes = usedRamMBytes * 1024.0 * 1024.0;

            status.GpuUsagePercent = GetGpuUsage();
            status.GpuTemperatureCelsius = GetGpuTemperature();

            // 상위 프로세스 (작업 집합 메모리 기준) — 접근 불가 프로세스는 건너뜀
            try {
                var rows = new List<ProcessInfoModel>();
                foreach (var p in Process.GetProcesses()) {
                    try {
                        if (string.IsNullOrEmpty(p.ProcessName)) continue;
                        string? exePath = null;
                        try {
                            exePath = p.MainModule?.FileName;
                        } catch {
                            /* 보호 프로세스 등 */
                        }

                        rows.Add(new ProcessInfoModel {
                            ProcessId = p.Id,
                            ProcessName = p.ProcessName,
                            MemoryWorkingSetBytes = p.WorkingSet64,
                            ExecutablePath = exePath,
                            IsSystemCritical = IsCritical(p.ProcessName)
                        });
                    } catch {
                        // 일부 시스템/보호 프로세스는 지표 읽기 실패
                    } finally {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }
                }

                // 각 프로세스 WS ÷ (현재 사용 중인 물리 RAM 바이트). 설치 전체 RAM 기준이 아님.
                status.TopProcesses = rows
                    .OrderByDescending(x => x.MemoryWorkingSetBytes)
                    .Take(8)
                    .Select(x => {
                        if (usedRamBytes > 0)
                            x.MemoryPercentOfUsedRam = Math.Min(100, Math.Round(x.MemoryWorkingSetBytes / usedRamBytes * 100.0, 1));
                        return x;
                    })
                    .ToList();
            } catch {
                status.TopProcesses = new List<ProcessInfoModel>();
            }

            return status;
        }

        private static readonly string[] _criticalProcessNames =
            { "svchost", "explorer", "System", "csrss", "wininit" };

        private static bool IsCritical(string name) =>
            _criticalProcessNames.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase));

        private double GetGpuTemperature() {
            foreach (var hardware in _computer.Hardware) {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd) {
                    hardware.Update();
                    var tempSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (tempSensor != null) return (double)tempSensor.Value.GetValueOrDefault();
                }
            }
            return 0;
        }

        /// <summary>인식된 NVIDIA/AMD GPU의 Load 센서 중 전역 최대값을 사용률(0~100%)으로 사용합니다.</summary>
        private double GetGpuUsage() {
            double best = 0;
            foreach (var hardware in _computer.Hardware) {
                if (hardware.HardwareType != HardwareType.GpuNvidia && hardware.HardwareType != HardwareType.GpuAmd)
                    continue;
                hardware.Update();
                foreach (var s in hardware.Sensors) {
                    if (s.SensorType != SensorType.Load || s.Value is null) continue;
                    var v = (double)s.Value.Value;
                    if (v > best) best = v;
                }
            }
            return best > 0 ? Math.Round(Math.Clamp(best, 0, 100), 1) : 0;
        }

        public void Dispose() {
            _computer?.Close();
            _cpuCounter?.Dispose();
            _availableRamCounter?.Dispose();
        }

        /// <summary>
        /// 블랙리스트 기반으로 프로세스를 정리합니다.
        /// 사용자 화이트리스트에 있는 프로세스는 제외합니다.
        /// </summary>
        public int OptimizeSystem(List<string> blackList, List<string> userWhiteList) {
            int clearedCount = 0;

            foreach (var name in blackList) {
                // 사용자 화이트리스트에 있으면 건너뜀
                if (userWhiteList.Any(w => w.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // 시스템 크리티컬 프로세스도 건너뜀
                if (IsCritical(name))
                    continue;

                try {
                    var processes = Process.GetProcessesByName(name);
                    foreach (var p in processes) {
                        p.Kill();
                        p.WaitForExit(1000);
                        clearedCount++;
                    }
                } catch {
                    continue;
                }
            }

            return clearedCount;
        }
    }
}

