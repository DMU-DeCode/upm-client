using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UPM.Communication;
using UPM.Core;
using UPM.Models;

namespace UPM.Desktop
{
    /// <summary>
    /// 화이트리스트(종료 제외 목록) 관리 창.
    /// 서버의 예외 목록 API(<see cref="ApiServerClient"/>)를 통해 등록/해제하며,
    /// 서버 미연결 시에는 로컬 파일(<see cref="LocalWhitelistStore"/>)로 폴백합니다.
    /// </summary>
    public partial class WhitelistWindow : Window
    {
        private readonly ApiServerClient _apiClient;
        private readonly HardwareMonitor _monitor;
        private readonly string _machineId;

        // 탭별 프로세스 목록 (HasVisibleWindow 기준으로 분리)
        private List<ProcessInfoModel> _windowedProcesses = new();
        private List<ProcessInfoModel> _backgroundProcesses = new();

        // 오른쪽 화이트리스트 목록. 서버 응답 또는 로컬 파일로 통째로 교체합니다.
        private readonly ObservableCollection<string> _whitelist = new();

        /// <param name="apiClient">예외 목록 API 호출용 클라이언트.</param>
        /// <param name="machineId">대상 머신 ID.</param>
        /// <param name="runningProcesses">현재 실행 중인 프로세스 목록(왼쪽 목록에 표시).</param>
        /// <param name="monitor">화이트리스트 변경 시 인메모리 예외 목록도 즉시 갱신하기 위한 모니터 인스턴스.</param>
        public WhitelistWindow(ApiServerClient apiClient, string machineId, List<ProcessInfoModel> runningProcesses, HardwareMonitor monitor)
        {
            InitializeComponent();

            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
            _machineId = string.IsNullOrWhiteSpace(machineId) ? Environment.MachineName : machineId;

            // 프로세스를 HasVisibleWindow 기준으로 분리
            var all = (runningProcesses ?? new List<ProcessInfoModel>())
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _windowedProcesses = all.Where(p => p.HasVisibleWindow).ToList();
            _backgroundProcesses = all.Where(p => !p.HasVisibleWindow).ToList();

            // 초기 탭: 열린 윈도우
            RunningProcessList.ItemsSource = _windowedProcesses;

            WhitelistList.ItemsSource = _whitelist;
        }

        private async void WhitelistWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            // 창 로드 시: 로컬 캐시를 즉시 표시하여 빠른 초기 렌더링 보장
            var cachedList = LocalWhitelistStore.Load();
            ReplaceWhitelist(cachedList);

            // 백그라운드에서 서버 동기화 (UI 차단 없이)
            SetBusy(true);
            try
            {
                var exceptions = await _apiClient.GetExceptionsAsync(_machineId)
                    .ConfigureAwait(false);
                await Dispatcher.InvokeAsync(() =>
                {
                    // 서버 응답이 비어있으면 로컬 캐시를 유지 (덮어쓰지 않음)
                    // → 껐다 켜도 화이트리스트가 초기화되지 않음
                    if (exceptions != null && exceptions.Count > 0)
                    {
                        ReplaceWhitelist(exceptions);
                        LocalWhitelistStore.Save(exceptions);
                    }
                });
            }
            catch
            {
                // 서버 실패 시 이미 로컬 캐시가 표시되어 있으므로 추가 작업 불필요
            }
            finally
            {
                await Dispatcher.InvokeAsync(() => SetBusy(false));
            }
        }

        /// <summary>탭 전환 시 RunningProcessList ItemsSource 교체.</summary>
        private void ProcessTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is not TabControl) return;

            RunningProcessList.ItemsSource = TabBackground.IsSelected
                ? _backgroundProcesses
                : _windowedProcesses;
        }

        /// <summary>헤더 우측 적용 버튼 — 현재 화이트리스트를 서버와 로컬에 저장하고 창을 닫습니다.</summary>
        private async void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            SetBusy(true);
            try
            {
                var current = _whitelist.ToList();
                LocalWhitelistStore.Save(current);
                _monitor.SetUserExceptions(current);

                // 서버에도 동기화 (각 항목을 add 로 전송)
                foreach (var name in current)
                {
                    await _apiClient.UpdateUserExceptionAsync(_machineId, name, "add");
                }

                // 적용 완료 알림 후 창 닫기
                MessageBox.Show(
                    $"화이트리스트가 적용되었습니다.\n총 {current.Count}개 항목이 보호됩니다.",
                    "적용 완료",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UPM] 화이트리스트 적용 실패: {ex.Message}");
                MessageBox.Show($"적용 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>상단 TextBox에 직접 입력한 프로세스를 수동 등록합니다.</summary>
        private void AddManualButton_Click(object sender, RoutedEventArgs e)
        {
            var name = ManualInput.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            AddAsync(name);
            ManualInput.Clear();
        }

        private void ManualInput_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            var name = ManualInput.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            AddAsync(name);
            ManualInput.Clear();
        }

        /// <summary>왼쪽에서 선택한 실행 중 프로세스를 화이트리스트에 추가합니다.</summary>
        private void AddSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (RunningProcessList.SelectedItem is not ProcessInfoModel selected) return;
            if (string.IsNullOrWhiteSpace(selected.ProcessName)) return;

            AddAsync(selected.ProcessName);
        }

        /// <summary>오른쪽에서 선택한 화이트리스트 항목을 제거합니다.</summary>
        private void RemoveSelectedButton_Click(object sender, RoutedEventArgs e)
        {
            if (WhitelistList.SelectedItem is not string selected) return;
            if (string.IsNullOrWhiteSpace(selected)) return;

            // UPM 자체는 제거 불가
            if (selected.Equals("UPM.Desktop", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("UPM.Desktop은 항상 보호됩니다.", "제거 불가", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 로컬 즉시 반영 (서버는 백그라운드 동기화)
            var localUpdated = LocalWhitelistStore.Remove(selected);
            ReplaceWhitelist(localUpdated);
            // 인메모리 예외 목록 즉시 갱신
            _monitor.SetUserExceptions(localUpdated);

            // 백그라운드 서버 동기화
            _ = _apiClient.UpdateUserExceptionAsync(_machineId, selected, "remove");
        }

        private void AddAsync(string processName)
        {
            SetBusy(true);
            try
            {
                // 서버 재조회 없이 로컬 즉시 반영 → 응답속도 개선 (API 호출 1회→1회로 유지)
                var localUpdated = LocalWhitelistStore.Add(processName);
                ReplaceWhitelist(localUpdated);
                // 인메모리 예외 목록 즉시 갱신 (핵심: 이 줄이 없으면 최적화 실행 시 화이트리스트 무시됨)
                _monitor.SetUserExceptions(localUpdated);

                // 백그라운드 서버 동기화 (실패해도 로컬은 이미 반영됨)
                _ = _apiClient.UpdateUserExceptionAsync(_machineId, processName, "add");
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>서버 응답 또는 로컬 목록으로 오른쪽 화이트리스트를 통째로 교체합니다.</summary>
        private void ReplaceWhitelist(List<string> items)
        {
            _whitelist.Clear();
            if (items != null)
            {
                foreach (var name in items)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        _whitelist.Add(name);
                }
            }
            WhitelistEmptyHint.Visibility = _whitelist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>API 호출 중에는 모든 조작 버튼과 입력을 비활성화합니다.</summary>
        private void SetBusy(bool busy)
        {
            bool enabled = !busy;
            AddManualButton.IsEnabled = enabled;
            AddSelectedButton.IsEnabled = enabled;
            RemoveSelectedButton.IsEnabled = enabled;
            ApplyButton.IsEnabled = enabled;
            ManualInput.IsEnabled = enabled;
            RunningProcessList.IsEnabled = enabled;
            WhitelistList.IsEnabled = enabled;
        }
    }
}
