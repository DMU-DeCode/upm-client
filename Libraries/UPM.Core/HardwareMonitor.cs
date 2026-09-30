using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using UPM.Models;
using UPM.Communication;
using LibreHardwareMonitor.Hardware;

namespace UPM.Core
{
    public class HardwareMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _availableRamCounter;
        private readonly Computer _computer;
        private double _totalRamMBytes;

        public HardwareMonitor()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();

            _availableRamCounter = new PerformanceCounter("Memory", "Available MBytes");
            _availableRamCounter.NextValue();

            _computer = new Computer { IsGpuEnabled = true };
            _computer.Open();
        }

        // CPU-Z처럼 상세 사양 정보를 가져오는 메서드
        public HardwareSpecModel GetHardwareSpecs()
        {
            var specs = new HardwareSpecModel();

            try
            {
                // 1. CPU 정보 (이름, 코어, 스레드)
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        specs.CpuName = obj["Name"]?.ToString() ?? "Unknown CPU";
                        specs.CpuCores = Convert.ToInt32(obj["NumberOfCores"]);
                        specs.CpuThreads = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                    }
                }

                // 2. RAM 총량
                using (var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        double kb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                        specs.RamTotal = $"{Math.Round(kb / (1024 * 1024), 0)} GB";
                        _totalRamMBytes = kb / 1024;
                    }
                }

                // 3. GPU 이름
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        specs.GpuName = hardware.Name;
                    }
                }

                // 4. 메인보드 정보
                using (var searcher = new ManagementObjectSearcher("SELECT Product, Manufacturer FROM Win32_BaseBoard"))
                {
                    foreach (var obj in searcher.Get())
                    {
                        specs.Mainboard = $"{obj["Manufacturer"]} {obj["Product"]}";
                    }
                }
            }
            catch { /* 예외 처리 */ }

            return specs;
        }

        public SystemStatusModel GetCurrentStatus()
        {
            var status = new SystemStatusModel
            {
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
            try
            {
                var rows = new List<ProcessInfoModel>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        if (string.IsNullOrEmpty(p.ProcessName)) continue;
                        string? exePath = GetProcessExecutablePath(p);

                        bool hasWindow = false;
                        try
                        {
                            hasWindow = p.MainWindowHandle != IntPtr.Zero;
                        }
                        catch { /* ignore */ }

                        var procModel = new ProcessInfoModel
                        {
                            ProcessId = p.Id,
                            ProcessName = p.ProcessName,
                            MemoryWorkingSetBytes = p.WorkingSet64,
                            ExecutablePath = exePath,
                            IsSystemCritical = IsCritical(p.ProcessName),
                            HasVisibleWindow = hasWindow,
                            IsProtected = IsProcessProtected(p.ProcessName),
                            ProcessCount = 1
                        };

                        rows.Add(procModel);
                    }
                    catch
                    {
                        // 일부 시스템/보호 프로세스는 지표 읽기 실패
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }
                }

                // 프로세스 이름 단위 항상 그룹화 (메모리 점유 합산 및 인스턴스 개수 집계)
                List<ProcessInfoModel> processedRows = rows
                    .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var firstWithExe = g.FirstOrDefault(x => !string.IsNullOrEmpty(x.ExecutablePath)) ?? g.First();
                        return new ProcessInfoModel
                        {
                            ProcessId = firstWithExe.ProcessId,
                            ProcessName = g.Key,
                            MemoryWorkingSetBytes = g.Sum(x => x.MemoryWorkingSetBytes),
                            ExecutablePath = firstWithExe.ExecutablePath,
                            IsSystemCritical = IsCritical(g.Key),
                            HasVisibleWindow = g.Any(x => x.HasVisibleWindow),
                            IsProtected = IsProcessProtected(g.Key),
                            ProcessCount = g.Count()
                        };
                    })
                    .OrderByDescending(x => x.MemoryWorkingSetBytes)
                    .Take(8)
                    .ToList();

                // 각 프로세스(또는 그룹) WS ÷ (현재 사용 중인 물리 RAM 바이트)
                status.TopProcesses = processedRows
                    .Select(x =>
                    {
                        if (usedRamBytes > 0)
                            x.MemoryPercentOfUsedRam = Math.Min(100, Math.Round(x.MemoryWorkingSetBytes / usedRamBytes * 100.0, 1));
                        return x;
                    })
                    .ToList();
            }
            catch
            {
                status.TopProcesses = new List<ProcessInfoModel>();
            }

            return status;
        }

        private static readonly string[] _criticalProcessNames = {
            // Windows 필수 시스템 프로세스
            "svchost", "explorer", "System", "csrss", "wininit", "lsass", "smss", "services",
            "dwm", "winlogon", "fontdrvhost", "conhost", "RuntimeBroker",
            "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv",
            "spoolsv", "wlanext", "WmiPrvSE", "dllhost", "sihost", "taskhostw",
            "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
            "TextInputHost", "ctfmon", "Registry", "Idle", "Memory Compression",
            // UPM 자체 보호
            "UPM.Desktop"
        };

        private readonly HashSet<string> _userExcludedProcesses = new(StringComparer.OrdinalIgnoreCase);

        public bool AutoCloseMode { get; set; } = true;
        public bool EnableProcessGrouping { get; set; } = true;

        public void SetUserExceptions(IEnumerable<string> exceptions)
        {
            lock (_userExcludedProcesses)
            {
                _userExcludedProcesses.Clear();
                foreach (var ex in exceptions)
                {
                    if (!string.IsNullOrWhiteSpace(ex))
                    {
                        _userExcludedProcesses.Add(ex);
                    }
                }
                // UPM 자체는 항상 보호 (껐다 켜도 유지)
                _userExcludedProcesses.Add("UPM.Desktop");
            }
        }

        public bool ToggleProcessProtection(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName) || IsCritical(processName))
                return true;

            bool isNowProtected;
            lock (_userExcludedProcesses)
            {
                if (_userExcludedProcesses.Contains(processName))
                {
                    _userExcludedProcesses.Remove(processName);
                    isNowProtected = false;
                }
                else
                {
                    _userExcludedProcesses.Add(processName);
                    isNowProtected = true;
                }
            }

            // 영속성 보장: 인메모리 변경을 로컬 파일에 반영
            if (isNowProtected)
                LocalWhitelistStore.Add(processName);
            else
                LocalWhitelistStore.Remove(processName);

            return isNowProtected;
        }

        public bool IsProcessProtected(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return false;
            if (IsCritical(processName))
                return true;

            lock (_userExcludedProcesses)
            {
                return _userExcludedProcesses.Contains(processName);
            }
        }

        private static bool IsCritical(string name) =>
            _criticalProcessNames.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase));

        private double GetGpuTemperature()
        {
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    hardware.Update();
                    var tempSensor = hardware.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
                    if (tempSensor != null) return (double)tempSensor.Value.GetValueOrDefault();
                }
            }
            return 0;
        }

        /// <summary>인식된 NVIDIA/AMD GPU의 Load 센서 중 전역 최대값을 사용률(0~100%)으로 사용합니다.</summary>
        private double GetGpuUsage()
        {
            double best = 0;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType != HardwareType.GpuNvidia && hardware.HardwareType != HardwareType.GpuAmd)
                    continue;
                hardware.Update();
                foreach (var s in hardware.Sensors)
                {
                    if (s.SensorType != SensorType.Load || s.Value is null) continue;
                    var v = (double)s.Value.Value;
                    if (v > best) best = v;
                }
            }
            return best > 0 ? Math.Round(Math.Clamp(best, 0, 100), 1) : 0;
        }

        /// <summary>
        /// 현재 실행 중인 모든 프로세스를 그룹화하여 반환합니다 (상위 8개 제한 없음).
        /// 화이트리스트 창 등 전체 프로세스 목록이 필요한 곳에 사용합니다.
        /// </summary>
        public List<ProcessInfoModel> GetAllRunningProcesses()
        {
            var rows = new List<ProcessInfoModel>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (string.IsNullOrEmpty(p.ProcessName)) continue;
                    string? exePath = GetProcessExecutablePath(p);

                    bool hasWindow = false;
                    try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { /* ignore */ }

                    rows.Add(new ProcessInfoModel
                    {
                        ProcessId = p.Id,
                        ProcessName = p.ProcessName,
                        MemoryWorkingSetBytes = p.WorkingSet64,
                        ExecutablePath = exePath,
                        IsSystemCritical = IsCritical(p.ProcessName),
                        HasVisibleWindow = hasWindow,
                        IsProtected = IsProcessProtected(p.ProcessName),
                        ProcessCount = 1
                    });
                }
                catch
                {
                    // 일부 시스템/보호 프로세스는 지표 읽기 실패
                }
                finally
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }

            // 프로세스 이름 단위 그룹화 (메모리 점유 합산 및 인스턴스 개수 집계)
            return rows
                .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var firstWithExe = g.FirstOrDefault(x => !string.IsNullOrEmpty(x.ExecutablePath)) ?? g.First();
                    return new ProcessInfoModel
                    {
                        ProcessId = firstWithExe.ProcessId,
                        ProcessName = g.Key,
                        MemoryWorkingSetBytes = g.Sum(x => x.MemoryWorkingSetBytes),
                        ExecutablePath = firstWithExe.ExecutablePath,
                        IsSystemCritical = IsCritical(g.Key),
                        HasVisibleWindow = g.Any(x => x.HasVisibleWindow),
                        IsProtected = IsProcessProtected(g.Key),
                        ProcessCount = g.Count()
                    };
                })
                .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Dispose()
        {
            _computer?.Close();
            _cpuCounter?.Dispose();
            _availableRamCounter?.Dispose();
        }

        /// <summary>
        /// 단일 프로세스를 PID로 안전하게 종료합니다.
        /// </summary>
        public bool KillProcessById(int processId, out string processName)
        {
            processName = "";
            try
            {
                using var p = Process.GetProcessById(processId);
                processName = p.ProcessName;
                if (IsCritical(processName))
                    return false;

                p.Kill();
                p.WaitForExit(1000);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 특정 프로세스 이름의 모든 인스턴스를 일괄 종료합니다.
        /// </summary>
        public int KillProcessGroupByName(string processName, out List<string> killedNames)
        {
            killedNames = new List<string>();
            if (string.IsNullOrWhiteSpace(processName) || IsCritical(processName))
                return 0;

            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill();
                        p.WaitForExit(1000);
                        killedNames.Add(processName);
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }

            return killedNames.Count;
        }

        /// <summary>
        /// 프로세스를 정리하여 시스템 메모리를 최적화합니다 (비동기, 서버 연동 및 예외/모드 옵션 반영).
        /// </summary>
        public async Task<int> OptimizeSystemAsync(ApiServerClient? apiClient = null, string machineId = "", List<ProcessInfoModel>? currentProcesses = null)
        {
            var killedNames = new List<string>();
            bool serverSuccess = false;

            if (AutoCloseMode)
            {
                // 1. 서버 연동 스마트 최적화 시도
                // serverSuccess = 서버가 정상 응답(null이 아닌 리스트)을 줬는지 여부.
                // killList가 빈 배열이어도(종료 대상 없음) 서버 통신은 성공한 것이므로
                // 로컬 폴백(2번)으로 떨어지지 않도록 반드시 true로 세팅해야 합니다.
                if (apiClient != null && !string.IsNullOrEmpty(machineId) && currentProcesses != null && currentProcesses.Count > 0)
                {
                    try
                    {
                        var killList = await apiClient.AnalyzeOptimizationAsync(machineId, currentProcesses);
                        // null = 통신 실패/예외, 빈 리스트 = 서버 응답 성공(종료 대상 없음)
                        if (killList != null)
                        {
                            serverSuccess = true; // 빈 리스트여도 서버 통신 성공으로 처리
                            foreach (var name in killList)
                            {
                                // 필수 시스템 프로세스 및 UPM 자체 보호 (IsCritical 포함)
                                if (IsCritical(name)) continue;
                                if (IsProcessProtected(name)) continue;
                                try
                                {
                                    foreach (var p in Process.GetProcessesByName(name))
                                    {
                                        p.Kill();
                                        p.WaitForExit(1000);
                                        killedNames.Add(name);
                                    }
                                }
                                catch { /* ignore */ }
                            }
                        }
                        // killList == null 이면 serverSuccess = false 유지 → 로컬 폴백
                    }
                    catch { /* 서버 분석 실패 시 로컬 로직으로 폴백 */ }
                }

                // 2. 서버 미연결 시: 로컬 화이트리스트 + 필수 프로세스를 제외한 모든 프로세스 종료
                if (!serverSuccess)
                {
                    var localWhitelist = LocalWhitelistStore.Load();
                    var whitelistSet = new HashSet<string>(localWhitelist, StringComparer.OrdinalIgnoreCase);

                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(p.ProcessName)) continue;
                            // 필수 시스템 프로세스 건너뜀
                            if (IsCritical(p.ProcessName)) continue;
                            // 사용자 보호(화이트리스트) 프로세스 건너뜀
                            if (whitelistSet.Contains(p.ProcessName)) continue;
                            // HardwareMonitor 내부 _userExcludedProcesses도 확인
                            if (IsProcessProtected(p.ProcessName)) continue;

                            p.Kill();
                            p.WaitForExit(1000);
                            killedNames.Add(p.ProcessName);
                        }
                        catch
                        {
                            // 일부 프로세스는 권한 부족 등으로 종료 실패 가능
                        }
                        finally
                        {
                            try { p.Dispose(); } catch { /* ignore */ }
                        }
                    }
                }
            }
            else
            {
                // 선택 종료 모드: 사용자가 선택한 프로세스만 종료
                if (currentProcesses != null)
                {
                    foreach (var proc in currentProcesses)
                    {
                        if (proc.IsSelectedForKill && !IsProcessProtected(proc.ProcessName))
                        {
                            try
                            {
                                using var p = Process.GetProcessById(proc.ProcessId);
                                p.Kill();
                                p.WaitForExit(1000);
                                killedNames.Add(proc.ProcessName);
                            }
                            catch { /* ignore */ }
                        }
                    }
                }
            }

            // 서버가 연결된 경우 종료 이력 리포팅
            if (apiClient != null && !string.IsNullOrEmpty(machineId) && killedNames.Count > 0)
            {
                try
                {
                    await apiClient.ReportOptimizationAsync(machineId, killedNames);
                }
                catch { /* 서버 미연결 시 리포팅 실패 무시 */ }
            }

            return killedNames.Count;
        }

        /// <summary>
        /// 동기식 최적화 백워드 호환 메서드
        /// </summary>
        public int OptimizeSystem()
        {
            return OptimizeSystemAsync().GetAwaiter().GetResult();
        }

        #region Win32 API Process Path Helper
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, [Out] StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        /// <summary>
        /// Process.MainModule 접근 권한(PROCESS_VM_READ) 부족 시 Win32 QueryFullProcessImageName 폴백
        /// </summary>
        private static string? GetProcessExecutablePath(Process p)
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path)) return path;
            }
            catch
            {
                /* MainModule 읽기 권한 없음 (보호/서비스 프로세스) */
            }

            try
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        var sb = new StringBuilder(1024);
                        int capacity = sb.Capacity;
                        if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            return sb.ToString();
                        }
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
            }
            catch { /* ignore */ }

            return null;
        }
        #endregion
    }
}

