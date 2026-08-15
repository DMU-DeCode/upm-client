using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UPM.Communication;
using UPM.Models;

namespace UPM.Desktop {
    /// <summary>
    /// 화이트리스트(종료 제외 목록) 관리 창.
    /// 서버의 예외 목록 API(<see cref="ApiServerClient"/>)를 통해 등록/해제하며,
    /// 응답으로 받은 갱신된 전체 목록으로 오른쪽 목록을 다시 채웁니다.
    /// </summary>
    public partial class WhitelistWindow : Window {
        private readonly ApiServerClient _apiClient;
        private readonly string _machineId;

        // 오른쪽 화이트리스트 목록. 서버 응답으로 통째로 교체합니다.
        private readonly ObservableCollection<string> _whitelist = new();

        /// <param name="apiClient">예외 목록 API 호출용 클라이언트.</param>
        /// <param name="machineId">대상 머신 ID.</param>
        /// <param name="runningProcesses">현재 실행 중인 프로세스 목록(왼쪽 목록에 표시).</param>
        public WhitelistWindow(ApiServerClient apiClient, string machineId, List<ProcessInfoModel> runningProcesses) {
            InitializeComponent();

            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _machineId = string.IsNullOrWhiteSpace(machineId) ? Environment.MachineName : machineId;

            // 왼쪽: 실행 중인 프로세스(이름순 정렬). 오른쪽: 화이트리스트 바인딩.
            RunningProcessList.ItemsSource = (runningProcesses ?? new List<ProcessInfoModel>())
                .OrderBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            WhitelistList.ItemsSource = _whitelist;
        }

        private async void WhitelistWindow_OnLoaded(object sender, RoutedEventArgs e) {
            // 창 로드 시 서버의 기존 화이트리스트를 불러와 표시합니다.
            SetBusy(true);
            try {
                var exceptions = await _apiClient.GetExceptionsAsync(_machineId);
                ReplaceWhitelist(exceptions);
            } catch (Exception ex) {
                MessageBox.Show(this, $"화이트리스트를 불러오지 못했습니다.\n{ex.Message}", "UPM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            } finally {
                SetBusy(false);
            }
        }

        /// <summary>상단 TextBox에 직접 입력한 프로세스를 수동 등록합니다.</summary>
        private async void AddManualButton_Click(object sender, RoutedEventArgs e) {
            var name = ManualInput.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            await AddAsync(name);
            ManualInput.Clear();
        }

        private async void ManualInput_OnKeyDown(object sender, KeyEventArgs e) {
            if (e.Key != Key.Enter) return;
            var name = ManualInput.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;

            await AddAsync(name);
            ManualInput.Clear();
        }

        /// <summary>왼쪽에서 선택한 실행 중 프로세스를 화이트리스트에 추가합니다.</summary>
        private async void AddSelectedButton_Click(object sender, RoutedEventArgs e) {
            if (RunningProcessList.SelectedItem is not ProcessInfoModel selected) return;
            if (string.IsNullOrWhiteSpace(selected.ProcessName)) return;

            await AddAsync(selected.ProcessName);
        }

        /// <summary>오른쪽에서 선택한 화이트리스트 항목을 제거합니다.</summary>
        private async void RemoveSelectedButton_Click(object sender, RoutedEventArgs e) {
            if (WhitelistList.SelectedItem is not string selected) return;
            if (string.IsNullOrWhiteSpace(selected)) return;

            SetBusy(true);
            try {
                var updated = await _apiClient.RemoveExceptionAsync(_machineId, selected);
                ReplaceWhitelist(updated);
            } catch (Exception ex) {
                MessageBox.Show(this, $"화이트리스트에서 제거하지 못했습니다.\n{ex.Message}", "UPM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            } finally {
                SetBusy(false);
            }
        }

        private async Task AddAsync(string processName) {
            SetBusy(true);
            try {
                var updated = await _apiClient.AddExceptionAsync(_machineId, processName);
                ReplaceWhitelist(updated);
            } catch (Exception ex) {
                MessageBox.Show(this, $"화이트리스트에 추가하지 못했습니다.\n{ex.Message}", "UPM",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            } finally {
                SetBusy(false);
            }
        }

        /// <summary>서버 응답으로 받은 갱신 목록으로 오른쪽 화이트리스트를 통째로 교체합니다.</summary>
        private void ReplaceWhitelist(List<string> items) {
            _whitelist.Clear();
            if (items != null) {
                foreach (var name in items) {
                    if (!string.IsNullOrWhiteSpace(name))
                        _whitelist.Add(name);
                }
            }
            WhitelistEmptyHint.Visibility = _whitelist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>API 호출 중에는 모든 조작 버튼과 입력을 비활성화합니다.</summary>
        private void SetBusy(bool busy) {
            bool enabled = !busy;
            AddManualButton.IsEnabled = enabled;
            AddSelectedButton.IsEnabled = enabled;
            RemoveSelectedButton.IsEnabled = enabled;
            ManualInput.IsEnabled = enabled;
            RunningProcessList.IsEnabled = enabled;
            WhitelistList.IsEnabled = enabled;
        }
    }
}
