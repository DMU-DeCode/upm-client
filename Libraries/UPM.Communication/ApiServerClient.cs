using System.Diagnostics;
using System.Net.Http.Json;
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

    /// <summary>서버에서 최적화 룰셋(블랙/화이트리스트)을 가져옵니다.</summary>
    public async Task<OptimizationRules?> GetOptimizationRulesAsync() {
        try {
            var rules = await _httpClient.GetFromJsonAsync<OptimizationRules>($"{_baseUrl}/api/v1/optimization/rules");
            return rules;
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] 룰셋 가져오기 실패: {ex.Message}");
            return null;
        }
    }

    /// <summary>사용자 예외(화이트리스트)를 서버에 등록/삭제합니다.</summary>
    public async Task<bool> UpdateExceptionAsync(string machineId, string processName, string action = "add") {
        try {
            var payload = new {
                machineId,
                processName,
                action
            };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/exceptions", payload);
            return response.IsSuccessStatusCode;
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] 예외 목록 업데이트 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>서버에서 현재 사용자 예외 목록을 가져옵니다.</summary>
    public async Task<List<string>> GetExceptionsAsync(string machineId) {
        try {
            var result = await _httpClient.GetFromJsonAsync<ExceptionResponse>(
                $"{_baseUrl}/api/v1/optimization/exceptions?machineId={machineId}");
            return result?.Exceptions ?? new List<string>();
        } catch (Exception ex) {
            Debug.WriteLine($"[UPM] 예외 목록 조회 실패: {ex.Message}");
            return new List<string>();
        }
    }

    // 응답 모델
    public class OptimizationRules {
        public List<string> BlackList { get; set; } = new();
        public List<string> WhiteList { get; set; } = new();
    }

    public class ExceptionResponse {
        public string MachineId { get; set; } = "";
        public List<string> Exceptions { get; set; } = new();
    }
    
    public void Dispose() => _httpClient.Dispose();
}
