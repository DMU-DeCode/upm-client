using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using UPM.Models;

namespace UPM.Communication;

/// <summary>메트릭을 UPM_Server(FastAPI)로 전송하는 HTTP 클라이언트.</summary>
public class ApiServerClient : IDisposable {
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public ApiServerClient(string baseUrl) {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
    }

    /// <summary>실시간 시스템 상태를 <c>POST /api/v1/metrics</c> 로 전송합니다.</summary>
    public async Task<bool> SendSystemStatusAsync(SystemStatusModel status) {
        try {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/metrics", status);
            return response.IsSuccessStatusCode;
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] Metrics 전송 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 최적화 분석을 <c>POST /api/v1/optimization/analyze</c> 로 요청합니다.
    /// machineId와 현재 프로세스 목록을 전송하고, 종료 대상 프로세스 목록(killList)을 반환합니다.
    /// </summary>
    /// <returns>종료 대상 프로세스 이름 목록. 실패 시 빈 리스트.</returns>
    public async Task<List<string>> RequestOptimizationAsync(string machineId, List<ProcessInfoModel> topProcesses) {
        try {
            var request = new OptimizationRequest {
                MachineId = machineId,
                TopProcesses = topProcesses,
            };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/analyze", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OptimizationResponse>();
            return result?.KillList ?? new List<string>();
        } catch (Exception ex) {
            Console.WriteLine($"[UPM] 최적화 분석 요청 실패: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// 실제로 종료에 성공한 프로세스 목록을 <c>POST /api/v1/optimization/report</c> 로 보고합니다.
    /// 실패해도 앱 동작에 영향을 주지 않도록 예외를 삼키고 로깅만 합니다.
    /// </summary>
    public async Task ReportKilledProcessesAsync(string machineId, List<string> killedProcesses) {
        try {
            var request = new KilledProcessesReport {
                MachineId = machineId,
                KilledProcesses = killedProcesses ?? new List<string>(),
            };
            await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/report", request);
        } catch (Exception ex) {
            Console.WriteLine($"[UPM] 종료 프로세스 보고 실패: {ex.Message}");
        }
    }

    /// <summary>최적화 분석 요청 페이로드.</summary>
    private class OptimizationRequest {
        [JsonPropertyName("machineId")]
        public string MachineId { get; set; } = "";

        [JsonPropertyName("topProcesses")]
        public List<ProcessInfoModel> TopProcesses { get; set; } = new();
    }

    /// <summary>최적화 분석 응답. <c>{ "killList": [...] }</c> 형식.</summary>
    private class OptimizationResponse {
        [JsonPropertyName("killList")]
        public List<string> KillList { get; set; } = new();
    }

    /// <summary>종료 프로세스 보고 페이로드. <c>{ "machineId": ..., "killedProcesses": [...] }</c> 형식.</summary>
    private class KilledProcessesReport {
        [JsonPropertyName("machineId")]
        public string MachineId { get; set; } = "";

        [JsonPropertyName("killedProcesses")]
        public List<string> KilledProcesses { get; set; } = new();
    }

    public void Dispose() => _httpClient.Dispose();
}
