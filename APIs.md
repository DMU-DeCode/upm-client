# UPM — HTTP API

## 데스크톱 → 서버 (UPM_Server)

`ApiServerClient`가 전송하는 엔드포인트입니다.

| 메서드 | 경로 | 본문 | 설명 |
|:---:|:---|:---|:---|
| `POST` | `{baseUrl}/api/v1/metrics` | `SystemStatusModel` (JSON) | CPU·RAM·GPU·상위 프로세스 (약 5초 간격) |

기본 `baseUrl`: `UpmAppSettings.MetricsServerBaseUrl` (`http://localhost:8000`)

### `SystemStatusModel` 필드

| JSON 필드 | 타입 | 설명 |
|:---|:---|:---|
| `machineId` | string | 호스트명 |
| `cpuUsagePercent` | number | CPU 사용률 (%) |
| `ramUsagePercent` | number | RAM 사용률 (%) |
| `gpuUsagePercent` | number | GPU 사용률 (%) |
| `gpuTemperatureCelsius` | number | GPU 온도 (°C) |
| `timestamp` | string (ISO 8601) | 수집 시각 |
| `topProcesses` | array | `processId`, `processName`, `memoryWorkingSetBytes`, `memoryPercentOfUsedRam`, `isSystemCritical` |

---

## 모바일 → 서버 → 데스크톱

[UPM_Mobile](https://github.com/DMU-DeCode/UPM_Mobile) → ngrok → **UPM_Server :8000** → **데스크톱 에이전트 :8787**

| 메서드 | 서버 경로 | 데스크톱 (`http://127.0.0.1:8787`) | 설명 |
|:---:|:---|:---|:---|
| `POST` | `/shutdown` | `/shutdown` | Windows 종료 예약 (10초, `shutdown /a` 취소) |
| `POST` | `/optimize` | `/optimize` | `HardwareMonitor.OptimizeSystem()` |

포트·URL 변경: `Libraries/UPM.Core/UpmAppSettings.cs`

---

## 연동 저장소 (DeCode)

| 저장소 | 역할 |
|:---|:---|
| **UPM_Decode** (이 repo) | WPF 데스크톱 에이전트 |
| [UPM_Server](https://github.com/DMU-DeCode) | FastAPI + MySQL |
| [UPM_Mobile](https://github.com/DMU-DeCode/UPM_Mobile) | Flutter 모니터·원격 제어 |
