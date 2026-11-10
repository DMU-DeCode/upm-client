using System;
using System.IO;
using System.Text.Json;

namespace UPM.Core;

/// <summary>UPM 데스크톱·연동 기본 설정 (환경별 변경 시 이 파일 또는 %LocalAppData%/UPM/server.json 수정).</summary>
public static class UpmAppSettings
{
    /// <summary>메트릭 전송 대상 기본값 (UPM_Server FastAPI / ngrok 터널 주소).</summary>
    public const string MetricsServerBaseUrl = "https://attendee-sequence-blooper.ngrok-free.dev";

    /// <summary>모바일 제어 프록시가 전달하는 로컬 HTTP 포트.</summary>
    public const int MobileControlPort = 8787;

    /// <summary>로컬 설정 및 화이트리스트 저장 폴더 (%LocalAppData%/UPM).</summary>
    public static readonly string LocalDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UPM");

    /// <summary>로컬 화이트리스트 파일 경로 (%LocalAppData%/UPM/whitelist.json).</summary>
    public static readonly string WhitelistFilePath = Path.Combine(LocalDataDirectory, "whitelist.json");

    /// <summary>앱 설정(테마·서버 URL 등) 파일 경로 (%LocalAppData%/UPM/settings.json).</summary>
    public static readonly string SettingsFilePath = Path.Combine(LocalDataDirectory, "settings.json");

    /// <summary>
    /// 서버 주소 설정 파일 경로 (%LocalAppData%/UPM/server.json).
    /// 예: { "serverUrl": "http://192.168.0.50:8000" } 또는 ngrok URL
    /// </summary>
    public static readonly string ServerSettingsFilePath = Path.Combine(LocalDataDirectory, "server.json");

    /// <summary>
    /// 스트림덱 MQTT 트리거 설정 파일 경로 (%LocalAppData%/UPM/mqtt.json).
    /// 파일이 없으면 아래 기본값을 사용합니다. 설치본에서 브로커 IP를 바꿀 때 이 파일만 수정하면 됩니다.
    /// </summary>
    public static readonly string MqttSettingsFilePath = Path.Combine(LocalDataDirectory, "mqtt.json");

    /// <summary>MQTT 브로커(라즈베리파이) 기본 주소.</summary>
    public const string MqttBrokerHost = "192.168.1.100";

    /// <summary>MQTT 브로커 기본 포트.</summary>
    public const int MqttBrokerPort = 1883;

    /// <summary>빠른 최적화 트리거 토픽 (스트림덱 스크립트와 동일해야 함).</summary>
    public const string MqttBoostTopic = "upm/control/boost";

    /// <summary>빠른 최적화 실행 페이로드.</summary>
    public const string MqttBoostPayload = "EXECUTE";

    /// <summary>
    /// 환경변수(UPM_SERVER_URL) → %LocalAppData%/UPM/server.json → %LocalAppData%/UPM/settings.json 순서로
    /// 서버 URL을 조회하며, 설정이 없으면 기본값(<see cref="MetricsServerBaseUrl"/>)을 반환합니다.
    /// </summary>
    public static string GetMetricsServerBaseUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("UPM_SERVER_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
            return envUrl.Trim().TrimEnd('/');

        foreach (var path in new[] { ServerSettingsFilePath, SettingsFilePath })
        {
            try
            {
                if (!File.Exists(path))
                    continue;

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals("serverUrl", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(val))
                            return val.Trim().TrimEnd('/');
                    }
                }
            }
            catch
            {
                // 파일 파싱 실패 시 다음 후보 또는 기본값으로 폴백
            }
        }

        return MetricsServerBaseUrl;
    }
}
