using System.Net.Http;
using System.Net.Http.Json;
using UPM.Models;

namespace UPM.Communication
{
    public class UpmApiService
    {
        private readonly HttpClient _client = new HttpClient();
        // TODO: 사용자님의 IPv4 주소로 변경하세요.
        private const string BaseUrl = "http://192.168.55.161:8000/api/v1";

        public async Task SendSpecsAsync(HardwareSpecModel specs)
        {
            try
            {
                await _client.PostAsJsonAsync($"{BaseUrl}/specs", specs);
            }
            catch { /* 서버가 꺼져있어도 앱이 멈추지 않게 무시 */ }
        }

        public async Task SendStatusAsync(SystemStatusModel status)
        {
            try
            {
                await _client.PostAsJsonAsync($"{BaseUrl}/status", status);
            }
            catch { /* 무시 */ }
        }
    }
}