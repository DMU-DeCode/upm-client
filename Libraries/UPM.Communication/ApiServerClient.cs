using System.Net.Http.Json;
using UPM.Models;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace UPM.Communication
{
    /// <summary>
    /// 하드웨어 정보를 외부 서버로 전송하는 클라이언트 클래스입니다.
    /// </summary>
    public class ApiServerClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public ApiServerClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient();

            // 🚨 [추가된 부분] ngrok HTML 경고창 우회 헤더 (필수)
            _httpClient.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "69420");
        }

        /// <summary>
        /// 하드웨어 상세 사양 정보를 서버에 등록합니다.
        /// </summary>
        public async Task<bool> SendHardwareSpecsAsync(HardwareSpecModel specs)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/hardware/specs", specs);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Specs 전송 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 실시간 시스템 상태 정보를 서버로 전송합니다.
        /// </summary>
        public async Task<bool> SendSystemStatusAsync(SystemStatusModel status)
        {
            try
            {
                // 엔드포인트가 /api/v1/metrics 로 잘 설정되어 있습니다.
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/v1/metrics", status);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Metrics 전송 실패: {ex.Message}");
                return false;
            }
        }
    }
}