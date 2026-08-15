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

                        // 가시 창 보유 여부 — 접근 거부 예외 대비 try/catch
                        bool hasVisibleWindow = false;
                        try {
                            hasVisibleWindow = p.MainWindowHandle != IntPtr.Zero;
                        } catch {
                            /* Access Denied 등 — false 유지 */
                        }

                        // (성능) 프로세스별 진단 로그는 초당 수백 줄 출력으로 Debug 빌드에서 지연을 유발하므로 제거했습니다.
                        // 필요 시 특정 프로세스만 조건부로 로깅하세요.

                        rows.Add(new ProcessInfoModel {
                            ProcessId = p.Id,
                            ProcessName = p.ProcessName,
                            MemoryWorkingSetBytes = p.WorkingSet64,
                            ExecutablePath = exePath,
                            IsSystemCritical = IsCritical(p.ProcessName),
                            HasVisibleWindow = hasVisibleWindow,
                            IsCurrentUserOwned = IsOwnedByCurrentUser(p),
                            // 초기값 0.0 — 아래 상위 N개 프로세스에 대해서만 실제 CPU 사용률을 측정해 갱신합니다.
                            CpuPercent = 0.0
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
                    .Take(40)
                    .Select(x => {
                        if (usedRamBytes > 0)
                            x.MemoryPercentOfUsedRam = Math.Min(100, Math.Round(x.MemoryWorkingSetBytes / usedRamBytes * 100.0, 1));
                        return x;
                    })
                    .ToList();

                // 성능 최적화: CPU 측정은 500ms 샘플링이라 40개 전부 측정하면 UI가 느려집니다.
                // 최적화용으로 목록은 40개까지 유지하되, CPU 사용률은 메모리 상위 8개에 대해서만 측정합니다.
                MeasureCpuUsage(status.TopProcesses.Take(8).ToList());
            } catch {
                status.TopProcesses = new List<ProcessInfoModel>();
            }

            return status;
        }

        /// <summary>
        /// 지정한 프로세스들의 CPU 사용률(%)을 두 시점의 <see cref="Process.TotalProcessorTime"/> 차이로 산출해
        /// 각 <see cref="ProcessInfoModel.CpuPercent"/>에 채웁니다.
        /// 공식: (CPU시간 증가분 ÷ (경과시간 × 논리코어수)) × 100.
        /// 접근 거부·프로세스 종료 등으로 측정에 실패하면 해당 항목은 0.0으로 둡니다.
        /// 성능을 위해 전체가 아닌 상위 N개(TopProcesses)에 대해서만 호출하세요.
        /// </summary>
        /// <param name="targets">CPU 사용률을 측정할 대상 목록(보통 메모리 상위 N개).</param>
        /// <param name="sampleIntervalMs">1·2차 샘플 사이 대기 시간(ms). 기본 500ms.</param>
        private static void MeasureCpuUsage(List<ProcessInfoModel> targets, int sampleIntervalMs = 500) {
            if (targets == null || targets.Count == 0) return;

            int coreCount = Environment.ProcessorCount;
            if (coreCount <= 0) coreCount = 1;

            // 1차 샘플: PID로 프로세스를 열어 누적 CPU 시간과 측정 시각을 기록합니다.
            var firstSample = new Dictionary<int, (TimeSpan cpuTime, DateTime at)>();
            foreach (var t in targets) {
                try {
                    using var p = Process.GetProcessById(t.ProcessId);
                    firstSample[t.ProcessId] = (p.TotalProcessorTime, DateTime.UtcNow);
                } catch {
                    // 이미 종료됐거나 접근 거부 — 측정 대상에서 제외(CpuPercent 0.0 유지)
                }
            }

            // 측정 간격 대기
            Thread.Sleep(sampleIntervalMs);

            // 2차 샘플: 증가분으로 CPU 사용률을 계산합니다.
            foreach (var t in targets) {
                if (!firstSample.TryGetValue(t.ProcessId, out var first)) continue;
                try {
                    using var p = Process.GetProcessById(t.ProcessId);
                    var secondCpu = p.TotalProcessorTime;
                    var elapsedMs = (DateTime.UtcNow - first.at).TotalMilliseconds;
                    if (elapsedMs <= 0) continue;

                    var cpuDeltaMs = (secondCpu - first.cpuTime).TotalMilliseconds;
                    var percent = cpuDeltaMs / (elapsedMs * coreCount) * 100.0;

                    // 반올림 오차·프로세스 재시작 등으로 인한 범위 이탈 보정
                    if (percent < 0) percent = 0;
                    if (percent > 100) percent = 100;

                    t.CpuPercent = Math.Round(percent, 1);
                } catch {
                    // 2차 측정 전 종료됐거나 접근 거부 — 0.0 유지
                }
            }
        }

        private static readonly string[] _criticalProcessNames =
            { "svchost", "explorer", "System", "csrss", "wininit" };

        private static bool IsCritical(string name) =>
            _criticalProcessNames.Any(c => name.Equals(c, StringComparison.OrdinalIgnoreCase));

        // 현재 실행 중인 UPM 앱 자신의 PID · 프로세스 이름 — 최적화 시 종료 대상에서 제외합니다.
        private static readonly int _selfProcessId = Process.GetCurrentProcess().Id;
        private static readonly string _selfProcessName = Process.GetCurrentProcess().ProcessName;

        // 현재 사용자(대화형) 세션 ID — 프로세스 소유 여부 판별 기준. SYSTEM/서비스는 보통 세션 0.
        private static readonly int _currentSessionId = Process.GetCurrentProcess().SessionId;

        /// <summary>
        /// 절대 종료 금지(하드코딩) 보호 목록. 서버 <c>HARD_PROTECT</c>와 동일하게 유지합니다.
        /// 서버가 실수로 killList에 포함시켜도 클라이언트가 최종적으로 거부하기 위한 이중 안전장치입니다.
        /// 대소문자·<c>.exe</c> 확장자는 무시하고 비교합니다. UPM 앱 자신의 이름도 포함합니다.
        /// </summary>
        private static readonly HashSet<string> _protectedProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                // Visual Studio / 빌드 툴체인
                "code", "devenv", "msbuild", "servicehub", "vbcscompiler", "perfwatson2",
                // 런타임 / 인터프리터 / 서버
                "python", "pythonw", "uvicorn", "node", "dotnet",
                // 셸 / 터미널 / VCS
                "cmd", "powershell", "pwsh", "conhost", "windowsterminal", "git", "ssh",
                // 시스템 필수 (서버 WHITELIST와 동일)
                "svchost", "explorer", "System", "csrss", "lsass", "winlogon",
                _selfProcessName, // 이 UPM 프로그램 자기 자신 (서버는 클라 자신의 이름을 알 수 없어 클라에서만 보호)
            };

        /// <summary>
        /// 프로세스 이름이 보호 목록에 있는지 여부. 확장자(.exe)가 제거된 이름을 넘겨야 합니다.
        /// </summary>
        private static bool IsProtected(string name) => _protectedProcessNames.Contains(name);

        /// <summary>
        /// 대상 프로세스가 현재 사용자 세션 소유인지 여부.
        /// SYSTEM/서비스 계정(세션 0 등 다른 세션)이면 false, 권한 예외 시에도 안전하게 false.
        /// </summary>
        private static bool IsOwnedByCurrentUser(Process p) {
            try {
                return _currentSessionId != 0 && p.SessionId == _currentSessionId;
            } catch {
                // Access Denied 등 — 안전하게 소유 아님으로 처리
                return false;
            }
        }

        /// <summary>대상 프로세스가 UPM 앱 자신인지 여부. 자기 자신은 절대 종료하지 않습니다.</summary>
        private static bool IsSelf(Process p) {
            try {
                if (p.Id == _selfProcessId) return true;
            } catch { /* 이미 종료된 프로세스 등 */ }
            return p.ProcessName.Equals(_selfProcessName, StringComparison.OrdinalIgnoreCase);
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
                        if (IsSelf(p)) { try { p.Dispose(); } catch { } continue; } // 자기 자신은 종료하지 않음
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

        /// <summary>
        /// 서버가 내려준 killList의 프로세스들만 종료합니다.
        /// 프로세스 이름에 <c>.exe</c> 확장자가 있든 없든 모두 처리합니다.
        /// </summary>
        /// <param name="killList">종료할 프로세스 이름 목록(예: "chrome" 또는 "chrome.exe").</param>
        /// <returns>실제로 종료에 성공한 프로세스 이름 목록. 권한 오류 등으로 종료에 실패한 항목은 포함하지 않습니다.</returns>
        public List<string> OptimizeSystemByRules(List<string> killList) {
            var killedNames = new List<string>();
            if (killList == null || killList.Count == 0) return killedNames;

            foreach (var rawName in killList) {
                if (string.IsNullOrWhiteSpace(rawName)) continue;

                // Process.GetProcessesByName은 확장자 없는 이름을 요구하므로 .exe를 제거합니다.
                string name = rawName.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 4);

                // 이중 안전장치: 보호 목록에 있는 이름은 killList에 있더라도 종료하지 않고 건너뜁니다.
                if (IsProtected(name)) continue;

                try {
                    var processes = Process.GetProcessesByName(name);
                    Debug.WriteLine($"[Kill] name={name}, foundProcesses={processes.Length}");

                    foreach (var p in processes) {
                        if (IsSelf(p)) { try { p.Dispose(); } catch { } continue; } // 자기 자신은 종료하지 않음
                        try {
                            p.Kill();
                            bool exited = p.WaitForExit(1000);
                            if (exited) {
                                // Kill 성공 + WaitForExit로 종료 확인된 경우에만 이름을 기록합니다.
                                Debug.WriteLine($"[Kill] name={name}, pid={p.Id} → 성공(종료 확인)");
                                killedNames.Add(name);
                            } else {
                                // Kill은 호출됐지만 제한 시간 내 종료가 확인되지 않음 — 목록에 넣지 않음
                                Debug.WriteLine($"[Kill] name={name}, pid={p.Id} → 실패(제한 시간 내 종료 미확인)");
                            }
                        } catch (Exception ex) {
                            // 권한이 없거나 이미 종료된 경우 등 — 목록에 절대 넣지 않음
                            Debug.WriteLine($"[Kill] name={name} → 실패({ex.GetType().Name}: {ex.Message})");
                        } finally {
                            try { p.Dispose(); } catch { /* ignore */ }
                        }
                    }
                } catch (Exception ex) {
                    // 프로세스 조회 실패 시 다음 항목으로 진행
                    Debug.WriteLine($"[Kill] name={name} → 조회 실패({ex.GetType().Name}: {ex.Message})");
                    continue;
                }
            }

            return killedNames;
        }
    }
}