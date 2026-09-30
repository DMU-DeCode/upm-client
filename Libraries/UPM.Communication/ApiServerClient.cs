using System.Diagnostics;
using System.Net.Http.Json;
using UPM.Models;

namespace UPM.Communication;

/// <summary>메트릭을 UPM_Server(FastAPI)로 전송하는 HTTP 클라이언트.</summary>
public class ApiServerClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public ApiServerClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _httpClient = new HttpClient();
        // 서버 미연결 또는 느린 응답 시 무기한 대기 방지
        _httpClient.Timeout = TimeSpan.FromSeconds(8);
    }

    /// <summary>실시간 시스템 상태를 <c>POST /api/v1/metrics</c> 로 전송합니다.</summary>
    public async Task<bool> SendSystemStatusAsync(SystemStatusModel status)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/metrics", status);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] Metrics 전송 실패: {ex.Message}");
            return false;
        }
    }

    /// <summary>서버에 저장된 머신별 사용자 예외(보호) 프로세스 목록을 조회합니다.</summary>
    public async Task<List<string>> GetUserExceptionsAsync(string machineId)
    {
        try
        {
            var res = await _httpClient.GetFromJsonAsync<ExceptionsResponse>($"{_baseUrl}/api/v1/optimization/exceptions?machineId={Uri.EscapeDataString(machineId)}");
            return res?.Exceptions ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 예외 목록 조회 실패: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>서버 DB에 특정 프로세스 예외(보호) 추가("add") 또는 삭제("remove")를 요청합니다.</summary>
    public async Task<bool> UpdateUserExceptionAsync(string machineId, string processName, string action = "add")
    {
        try
        {
            var payload = new { machineId, processName, action };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/exceptions", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 예외 {action} 실패 ({processName}): {ex.Message}");
            return false;
        }
    }

    /// <summary>서버에 현재 프로세스 목록을 보내고 스마트 최적화 종료 대상(killList)을 수신합니다.</summary>
    /// <returns>
    /// - <c>null</c>: 서버 통신 실패 또는 HTTP 에러 → 로컬 폴백 허용
    /// - 빈 리스트: 서버 응답 성공, 종료 대상 없음 → 로컬 폴백 사용 안 함
    /// - 항목이 있는 리스트: 서버가 종료 권장 목록 반환
    /// </returns>
    public async Task<List<string>?> AnalyzeOptimizationAsync(string machineId, List<ProcessInfoModel> topProcesses)
    {
        try
        {
            var payload = new { machineId, topProcesses };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/analyze", payload);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<AnalyzeResponse>();
                // 서버 성공: KillList가 없으면 빈 리스트 반환 (null 아님 → 로컬 폴백 방지)
                return result?.KillList ?? new List<string>();
            }
            // HTTP 에러 응답 → null 반환 (로컬 폴백 허용)
            Debug.WriteLine($"[UPM] 스마트 최적화 서버 응답 오류: {response.StatusCode}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 스마트 최적화 분석 요청 실패: {ex.Message}");
            return null; // 통신 실패 → null 반환 (로컬 폴백 허용)
        }
    }

    /// <summary>실제 종료된 프로세스 내역을 서버에 보고합니다.</summary>
    public async Task<bool> ReportOptimizationAsync(string machineId, List<string> killedProcesses)
    {
        if (killedProcesses == null || killedProcesses.Count == 0)
            return true;

        try
        {
            var payload = new { machineId, killedProcesses };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/report", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 최적화 결과 리포트 실패: {ex.Message}");
            return false;
        }
    }

    public async Task<List<string>> GetExceptionsAsync(string machineId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/v1/optimization/exceptions?machineId={machineId}");
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ExceptionsResponse>();
            return result?.Exceptions ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 예외 목록 조회 실패: {ex.Message}");
            return new List<string>();
        }
    }

    public async Task<List<string>> AddExceptionAsync(string machineId, string processName)
    {
        try
        {
            var request = new ExceptionMutationRequest
            {
                MachineId = machineId,
                ProcessName = processName,
                Action = "add",
            };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/exceptions", request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ExceptionsResponse>();
            return result?.Exceptions ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 예외 추가 실패: {ex.Message}");
            return new List<string>();
        }
    }

    public async Task<List<string>> RemoveExceptionAsync(string machineId, string processName)
    {
        try
        {
            var request = new ExceptionMutationRequest
            {
                MachineId = machineId,
                ProcessName = processName,
                Action = "remove",
            };
            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/optimization/exceptions", request);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ExceptionsResponse>();
            return result?.Exceptions ?? new List<string>();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UPM] 예외 삭제 실패: {ex.Message}");
            return new List<string>();
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private class ExceptionMutationRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("machineId")]
        public string MachineId { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("processName")]
        public string ProcessName { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("action")]
        public string Action { get; set; } = "";
    }

    private class ExceptionsResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("exceptions")]
        public List<string>? Exceptions { get; set; }
    }

    private class AnalyzeResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("killList")]
        public List<string>? KillList { get; set; }
    }
}
