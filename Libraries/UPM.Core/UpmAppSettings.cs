namespace UPM.Core;

/// <summary>UPM 데스크톱·연동 기본 설정 (환경별 변경 시 이 파일만 수정).</summary>
public static class UpmAppSettings {
    /// <summary>메트릭 전송 대상 (UPM_Server FastAPI).</summary>
    public const string MetricsServerBaseUrl = "http://localhost:8000";

    /// <summary>모바일 제어 프록시가 전달하는 로컬 HTTP 포트.</summary>
    public const int MobileControlPort = 8787;
}
