using System;
using System.IO;

namespace UPM.Core;

/// <summary>UPM 데스크톱·연동 기본 설정 (환경별 변경 시 이 파일만 수정).</summary>
public static class UpmAppSettings
{
    /// <summary>메트릭 전송 대상 (UPM_Server FastAPI).</summary>
    public const string MetricsServerBaseUrl = "http://localhost:8000";

    /// <summary>모바일 제어 프록시가 전달하는 로컬 HTTP 포트.</summary>
    public const int MobileControlPort = 8787;

    /// <summary>로컬 설정 및 화이트리스트 저장 폴더 (%LocalAppData%/UPM).</summary>
    public static readonly string LocalDataDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UPM");

    /// <summary>로컬 화이트리스트 파일 경로 (%LocalAppData%/UPM/whitelist.json).</summary>
    public static readonly string WhitelistFilePath = Path.Combine(LocalDataDirectory, "whitelist.json");

    /// <summary>앱 설정(테마 등) 파일 경로 (%LocalAppData%/UPM/settings.json).</summary>
    public static readonly string SettingsFilePath = Path.Combine(LocalDataDirectory, "settings.json");
}
