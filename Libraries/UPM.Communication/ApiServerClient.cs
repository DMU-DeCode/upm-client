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

    public void Dispose() => _httpClient.Dispose();
}
