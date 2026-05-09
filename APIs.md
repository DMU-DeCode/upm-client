# UPM — 클라이언트가 호출하는 HTTP API

데스크톱 앱(`UPM.Communication.ApiServerClient`)이 전송하는 엔드포인트입니다. 서버는 이 규격에 맞게 구현하면 됩니다.

| 메서드 | 경로 | 본문 모델 | 설명 |
|:---:|:---|:---|:---|
| `POST` | `{baseUrl}/api/hardware/specs` | `HardwareSpecModel` (JSON) | CPU/GPU/RAM/메인보드 등 사양 등록 (코드에서 주석 처리 시 미전송) |
| `POST` | `{baseUrl}/api/v1/metrics` | `SystemStatusModel` (JSON) | 실시간 CPU·RAM·GPU 온도, 상위 프로세스 등 메트릭 (약 5초 간격 시도) |

### `SystemStatusModel` 요약 필드

| JSON 필드 | 타입 | 설명 |
|:---|:---|:---|
| `machineId` | string | 호스트명 |
| `cpuUsagePercent` | number | CPU 사용률 (%) |
| `ramUsagePercent` | number | RAM 사용률 (%) |
| `gpuTemperatureCelsius` | number | GPU 온도 (°C, 센서 없으면 0에 가깝게) |
| `timestamp` | string (ISO 8601) | 수집 시각 |
| `topProcesses` | array | `processId`, `processName`, `memoryWorkingSetBytes`, `isSystemCritical` |

`baseUrl` 기본값은 `MainWindow.xaml.cs`에서 `http://localhost:8000` 으로 설정되어 있습니다.
