using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using UPM.Models;
using LibreHardwareMonitor.Hardware;

namespace UPM.Core {
    public class HardwareMonitor : IDisposable {
        private readonly PerformanceCounter _cpuCounter;
        private readonly Computer _computer;
        private double _totalRamMBytes;

        public HardwareMonitor() {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();

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

            using (var ramCounter = new PerformanceCounter("Memory", "Available MBytes")) {
                double availableRam = ramCounter.NextValue();
                status.RamUsagePercent = Math.Round(((_totalRamMBytes - availableRam) / _totalRamMBytes) * 100, 1);
            }

            status.GpuTemperatureCelsius = GetGpuTemperature();

            // 상위 프로세스 정보 추가 (메모리 사용량 기준 상위 3개)
            try {
                status.TopProcesses = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderByDescending(p => p.WorkingSet64)
                    .Take(3)
                    .Select(p => new ProcessInfoModel {
                        ProcessId = p.Id,
                        ProcessName = p.ProcessName,
                        MemoryWorkingSetBytes = p.WorkingSet64,
                        IsSystemCritical = IsCritical(p.ProcessName)
                    })
                    .ToList();
            } catch {
                status.TopProcesses = new List<ProcessInfoModel>();
            }

            return status;
        }

        private bool IsCritical(string name) {
            string[] criticals = { "svchost", "explorer", "System", "csrss", "wininit" };
            return criticals.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase));
        }

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

        public void Dispose() {
            _computer?.Close();
            _cpuCounter?.Dispose();
        }
        // UPM.Core -> HardwareMonitor.cs 내부에 추가

        /// <summary>
        /// 불필요한 프로세스를 정리하여 시스템 메모리를 최적화합니다.
        /// </summary>
        /// <returns>종료된 프로세스의 개수</returns>
        public int OptimizeSystem() {
            int clearedCount = 0;

            // 정리 대상 프로세스 리스트 (사용자가 필요에 따라 수정 가능)
            // 예: 메모리 점유가 높은 브라우저나 단순 계산기 등
            string[] targetProcesses = { "Notepad", "CalculatorApp", "msedge" };

            foreach (var name in targetProcesses) {
                try {
                    // 실행 중인 해당 이름의 프로세스들을 모두 찾음
                    var processes = Process.GetProcessesByName(name);
                    foreach (var p in processes) {
                        p.Kill(); // 프로세스 강제 종료
                        p.WaitForExit(1000); // 종료될 때까지 잠시 대기
                        clearedCount++;
                    }
                } catch (Exception) {
                    // 권한이 없거나 이미 종료된 경우 무시
                    continue;
                }
            }

            return clearedCount;
        }
    }
}

