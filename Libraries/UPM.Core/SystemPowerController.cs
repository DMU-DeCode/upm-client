using System.Diagnostics;

namespace UPM.Core;

/// <summary>
/// Windows 전원 제어(종료)를 수행합니다.
/// </summary>
public static class SystemPowerController {
    /// <summary>
    /// 시스템 종료를 예약합니다. 기본 10초 후 종료되며, CMD에서 <c>shutdown /a</c>로 취소할 수 있습니다.
    /// </summary>
    public static void InitiateShutdown(int delaySeconds = 10) {
        if (delaySeconds < 0)
            delaySeconds = 0;

        var args = $"/s /t {delaySeconds} /c \"UPM 모바일 앱에서 종료 요청\"";
        using var process = Process.Start(new ProcessStartInfo {
            FileName = "shutdown",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        if (process == null)
            throw new InvalidOperationException("shutdown 프로세스를 시작하지 못했습니다.");
    }
}
