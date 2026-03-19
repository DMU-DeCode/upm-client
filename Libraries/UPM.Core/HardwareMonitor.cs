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
            var status = new SystemStatusModel { Timestamp = DateTime.Now, PcName = Environment.MachineName };
            status.CpuUsage = Math.Round(_cpuCounter.NextValue(), 1);

            using (var ramCounter = new PerformanceCounter("Memory", "Available MBytes")) {
                double availableRam = ramCounter.NextValue();
                status.RamUsage = Math.Round(((_totalRamMBytes - availableRam) / _totalRamMBytes) * 100, 1);
            }

            status.GpuTemperature = GetGpuTemperature();
            return status;
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
    }
}