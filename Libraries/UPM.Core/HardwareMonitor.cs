using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using UPM.Communication;
using UPM.Models;

namespace UPM.Core
{
    public class HardwareMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _availableRamCounter;
        private readonly Computer _computer;
        private double _totalRamMBytes;
        private readonly int _currentSessionId;
        private readonly int _selfProcessId;

        public HardwareMonitor()
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();

            _availableRamCounter = new PerformanceCounter("Memory", "Available MBytes");
            _availableRamCounter.NextValue();

            _computer = new Computer { IsGpuEnabled = true };
            _computer.Open();

            try
            {
                using var self = Process.GetCurrentProcess();
                _selfProcessId = self.Id;
                _currentSessionId = self.SessionId;
            }
            catch
            {
                _selfProcessId = Environment.ProcessId;
                _currentSessionId = 1;
            }

            EnsureTotalRamInitialized();
        }

        private void EnsureTotalRamInitialized()
        {
            if (_totalRamMBytes > 0) return;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT TotalVisibleMemorySize FROM Win32_OperatingSystem");
                foreach (var obj in searcher.Get())
                {
                    double kb = Convert.ToDouble(obj["TotalVisibleMemorySize"]);
                    if (kb > 0)
                    {
                        _totalRamMBytes = kb / 1024.0;
                        return;
                    }
                }
            }
            catch { /* WMI 실패 시 GC 폴백 */ }

            try
            {
                var gcMem = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (gcMem > 0)
                    _totalRamMBytes = gcMem / (1024.0 * 1024.0);
            }
            catch { /* ignore */ }
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
                EnsureTotalRamInitialized();
                if (_totalRamMBytes > 0)
                {
                    specs.RamTotal = $"{Math.Round(_totalRamMBytes / 1024.0, 0)} GB";
                }

                // 3. GPU 이름 (NVIDIA, AMD, Intel 지원)
                foreach (var hardware in _computer.Hardware)
                {
                    if (IsGpuHardware(hardware.HardwareType))
                    {
                        specs.GpuName = hardware.Name;
                        break;
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
            EnsureTotalRamInitialized();

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

            var (gpuUsage, gpuTemp) = GetGpuMetrics();
            status.GpuUsagePercent = gpuUsage;
            status.GpuTemperatureCelsius = gpuTemp;

            // 상위 프로세스 (작업 집합 메모리 기준) — 접근 불가 프로세스는 건너뜀
            try
            {
                var rows = CollectRawProcesses();

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
                            IsCurrentUserOwned = g.Any(x => x.IsCurrentUserOwned),
                            IsProtected = IsProcessProtected(g.Key),
                            IsSelectedForKill = IsProcessSelectedForKill(g.Key),
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

        private List<ProcessInfoModel> CollectRawProcesses()
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

                    bool isUserOwned = false;
                    try { isUserOwned = p.SessionId == _currentSessionId && p.SessionId > 0; } catch { /* ignore */ }

                    rows.Add(new ProcessInfoModel
                    {
                        ProcessId = p.Id,
                        ProcessName = p.ProcessName,
                        MemoryWorkingSetBytes = p.WorkingSet64,
                        ExecutablePath = exePath,
                        IsSystemCritical = IsCritical(p.ProcessName) || p.Id == _selfProcessId,
                        HasVisibleWindow = hasWindow,
                        IsCurrentUserOwned = isUserOwned,
                        IsProtected = IsProcessProtected(p.ProcessName),
                        IsSelectedForKill = IsProcessSelectedForKill(p.ProcessName),
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
            return rows;
        }

        private static readonly string[] _criticalProcessNames = {
            // Windows 필수 시스템 프로세스
            "svchost", "explorer", "System", "csrss", "wininit", "lsass", "smss", "services",
            "dwm", "winlogon", "fontdrvhost", "conhost", "RuntimeBroker",
            "SecurityHealthService", "SecurityHealthSystray", "MsMpEng", "NisSrv",
            "spoolsv", "wlanext", "WmiPrvSE", "dllhost", "sihost", "taskhostw",
            "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost",
            "TextInputHost", "ctfmon", "Registry", "Idle", "Memory Compression",
            "audiodg", "lsaiso", "SgrmBroker", "dashost", "ApplicationFrameHost",
            "SystemSettings", "UserOOBEBroker", " LockApp", "Widgets", "SearchIndexer",
            // 개발 도구 및 주요 런타임 안전 보호 (로컬 폴백 시 개발 환경 크래시 방지)
            "devenv", "Code", "rider64", "MSBuild", "VBCSCompiler", "dotnet", "Antigravity",
            "language_server_windows_x64", "node", "QtWebEngineProcess",
            "powershell", "pwsh", "cmd", "WindowsTerminal", "OpenConsole", "git",
            // UPM 및 연동 하드웨어 컨트롤러 보호
            "UPM.Desktop", "UlanziDeck", "ulanzideck"
        };

        private readonly HashSet<string> _userExcludedProcesses = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _selectedForKillProcesses = new(StringComparer.OrdinalIgnoreCase);

        public bool AutoCloseMode { get; set; } = true;

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
                // UPM 및 연동 하드웨어는 항상 보호 (껐다 켜도 유지)
                _userExcludedProcesses.Add("UPM.Desktop");
                _userExcludedProcesses.Add("UlanziDeck");
                _userExcludedProcesses.Add("ulanzideck");
                _userExcludedProcesses.Add("Antigravity");
                _userExcludedProcesses.Add("language_server_windows_x64");
                _userExcludedProcesses.Add("node");
                _userExcludedProcesses.Add("QtWebEngineProcess");
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

            // 보호 설정 시 선택 종료 대상에서 해제
            if (isNowProtected)
            {
                SetProcessSelectedForKill(processName, false);
                LocalWhitelistStore.Add(processName);
            }
            else
            {
                LocalWhitelistStore.Remove(processName);
            }

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

        public void SetProcessSelectedForKill(string processName, bool isSelected)
        {
            if (string.IsNullOrWhiteSpace(processName) || IsCritical(processName))
                return;

            lock (_selectedForKillProcesses)
            {
                if (isSelected)
                    _selectedForKillProcesses.Add(processName);
                else
                    _selectedForKillProcesses.Remove(processName);
            }
        }

        public bool IsProcessSelectedForKill(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName) || IsCritical(processName))
                return false;

            lock (_selectedForKillProcesses)
            {
                return _selectedForKillProcesses.Contains(processName);
            }
        }

        private static bool IsCritical(string name) =>
            _criticalProcessNames.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase)) ||
            name.StartsWith("language_server", StringComparison.OrdinalIgnoreCase);

        private static bool IsGpuHardware(HardwareType type) =>
            type == HardwareType.GpuNvidia ||
            type == HardwareType.GpuAmd ||
            type == HardwareType.GpuIntel;

        /// <summary>인식된 GPU(NVIDIA/AMD/Intel)를 1회 Update하여 사용률(%)과 온도(°C)를 함께 반환합니다.</summary>
        private (double UsagePercent, double TemperatureCelsius) GetGpuMetrics()
        {
            double bestLoad = 0;
            double? temperature = null;

            foreach (var hardware in _computer.Hardware)
            {
                if (!IsGpuHardware(hardware.HardwareType))
                    continue;

                hardware.Update();
                foreach (var s in hardware.Sensors)
                {
                    if (s.Value is null) continue;

                    if (s.SensorType == SensorType.Load)
                    {
                        var v = (double)s.Value.Value;
                        if (v > bestLoad) bestLoad = v;
                    }
                    else if (s.SensorType == SensorType.Temperature && temperature is null)
                    {
                        temperature = (double)s.Value.Value;
                    }
                }
            }

            double usage = bestLoad > 0 ? Math.Round(Math.Clamp(bestLoad, 0, 100), 1) : 0;
            return (usage, temperature ?? 0);
        }

        /// <summary>
        /// 화이트리스트 관리 창 및 프로세스 관리 UI에서 완전히 숨길 내부 보호 프로세스 여부
        /// </summary>
        public static bool IsHiddenInternalProcess(string name) =>
            name.Equals("UPM.Desktop", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("UlanziDeck", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ulanzideck", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 현재 실행 중인 모든 프로세스를 그룹화하여 반환합니다 (상위 8개 제한 없음).
        /// 화이트리스트 창 등 전체 프로세스 목록이 필요한 곳에 사용합니다.
        /// (UPM.Desktop 및 UlanziDeck 등 내부 필수 프로세스는 목록에서 제외됨)
        /// </summary>
        public List<ProcessInfoModel> GetAllRunningProcesses()
        {
            var rows = CollectRawProcesses();

            // 프로세스 이름 단위 그룹화 (메모리 점유 합산 및 인스턴스 개수 집계)
            return rows
                .Where(x => !IsHiddenInternalProcess(x.ProcessName))
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
                        IsCurrentUserOwned = g.Any(x => x.IsCurrentUserOwned),
                        IsProtected = IsProcessProtected(g.Key),
                        IsSelectedForKill = IsProcessSelectedForKill(g.Key),
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
            if (processId == _selfProcessId)
                return false;

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
                        if (p.Id == _selfProcessId) continue;
                        p.Kill();
                        p.WaitForExit(1000);
                        killedNames.Add(processName);
                    }
                    catch { /* ignore */ }
                    finally
                    {
                        try { p.Dispose(); } catch { /* ignore */ }
                    }
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
                                if (IsCritical(name) || IsProcessProtected(name)) continue;
                                KillProcessGroupByName(name, out var killedGroup);
                                killedNames.AddRange(killedGroup);
                            }
                        }
                    }
                    catch { /* 서버 분석 실패 시 로컬 로직으로 폴백 */ }
                }

                // 2. 서버 미연결 시 안전한 로컬 폴백:
                // 현재 사용자 세션의 비필수·미보호 프로세스 중 메모리 점유 상위 항목(50MB 이상)만 선별 정리
                if (!serverSuccess)
                {
                    var localWhitelist = LocalWhitelistStore.Load();
                    var whitelistSet = new HashSet<string>(localWhitelist, StringComparer.OrdinalIgnoreCase);
                    const long minMemoryThresholdBytes = 50L * 1024 * 1024; // 50 MB

                    var candidates = CollectRawProcesses()
                        .Where(p => p.IsCurrentUserOwned &&
                                    !p.IsSystemCritical &&
                                    !whitelistSet.Contains(p.ProcessName) &&
                                    !IsProcessProtected(p.ProcessName))
                        .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .Select(g => new
                        {
                            ProcessName = g.Key,
                            TotalMemory = g.Sum(x => x.MemoryWorkingSetBytes)
                        })
                        .Where(g => g.TotalMemory >= minMemoryThresholdBytes)
                        .OrderByDescending(g => g.TotalMemory)
                        .Take(5)
                        .ToList();

                    foreach (var target in candidates)
                    {
                        KillProcessGroupByName(target.ProcessName, out var killedGroup);
                        killedNames.AddRange(killedGroup);
                    }
                }
            }
            else
            {
                // 선택 종료 모드: 사용자가 체크한 프로세스(그룹 포함)만 안전하게 종료
                List<string> selectedTargets;
                lock (_selectedForKillProcesses)
                {
                    selectedTargets = _selectedForKillProcesses.ToList();
                }

                if (selectedTargets.Count == 0 && currentProcesses != null)
                {
                    selectedTargets = currentProcesses
                        .Where(p => p.IsSelectedForKill)
                        .Select(p => p.ProcessName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                foreach (var targetName in selectedTargets)
                {
                    if (IsCritical(targetName) || IsProcessProtected(targetName))
                        continue;

                    if (KillProcessGroupByName(targetName, out var killedGroup) > 0)
                    {
                        killedNames.AddRange(killedGroup);
                        SetProcessSelectedForKill(targetName, false);
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
