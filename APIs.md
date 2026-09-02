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
| `topProcesses` | array | `processId`, `processName`, `memoryWorkingSetBytes`, `memoryPercentOfUsedRam`, `isSystemCritical`, `hasVisibleWindow`, `cpuPercent`, `isCurrentUserOwned` |

---

## 최적화 (QUICK BOOST) — 데스크톱 → 서버

`ApiServerClient`가 QUICK BOOST 및 화이트리스트 창에서 호출하는 엔드포인트입니다. `baseUrl`은 위와 동일합니다.

| 메서드 | 경로 | 클라이언트 메서드 | 설명 |
|:---:|:---|:---|:---|
| `POST` | `/api/v1/optimization/analyze` | `RequestOptimizationAsync` | 종료 대상 분석 요청 → `killList` 수신 |
| `POST` | `/api/v1/optimization/report` | `ReportKilledProcessesAsync` | 실제 종료된 프로세스 목록 보고 |
| `GET` | `/api/v1/optimization/exceptions` | `GetExceptionsAsync` | 화이트리스트(예외) 목록 조회 |
| `POST` | `/api/v1/optimization/exceptions` | `AddExceptionAsync` / `RemoveExceptionAsync` | 예외 프로세스 추가·제거 |

### `POST /api/v1/optimization/analyze`

머신의 현재 상위 프로세스를 보내고, 서버가 판단한 **종료 대상 이름 목록**을 받습니다.

**요청 본문**

```json
{
  "machineId": "DESKTOP-ABC123",
  "topProcesses": [
    {
      "processId": 1234,
      "processName": "chrome",
      "memoryWorkingSetBytes": 524288000,
      "memoryPercentOfUsedRam": 12.3,
      "isSystemCritical": false,
      "hasVisibleWindow": true,
      "cpuPercent": 3.5,
      "isCurrentUserOwned": true
    }
  ]
}
```

**응답 본문**

```json
{ "killList": ["someapp", "anotherapp"] }
```

- 정리할 대상이 없으면 `killList`는 **빈 배열 `[]`** 로 응답합니다.
- 클라이언트는 **HTTP 오류/응답 없음**과 **빈 배열**을 구분합니다. 통신 실패 시에만 로컬 규칙으로 폴백하고, 빈 배열이면 "정리할 프로세스가 없습니다"로 안내합니다.
- 서버는 시스템 필수 프로세스를 `killList`에 넣지 않아야 합니다. (클라이언트도 `_protectedProcessNames`로 최종 거부)

### `POST /api/v1/optimization/report`

실제로 종료된 프로세스 목록을 이력 저장용으로 보고합니다. 응답 본문은 사용하지 않으며, 실패해도 앱 동작에 영향이 없습니다.

**요청 본문**

```json
{
  "machineId": "DESKTOP-ABC123",
  "killedProcesses": ["someapp", "anotherapp"]
}
```

### `GET /api/v1/optimization/exceptions?machineId={machineId}`

해당 머신의 **화이트리스트(종료 제외 목록)** 를 조회합니다.

**응답 본문**

```json
{
  "machineId": "DESKTOP-ABC123",
  "exceptions": ["steam", "discord"]
}
```

### `POST /api/v1/optimization/exceptions`

화이트리스트에 프로세스를 추가/제거합니다. `action` 값으로 동작을 구분하며, 응답은 **갱신된 전체 목록**입니다(클라이언트는 이 목록으로 화면을 통째로 다시 채웁니다).

**요청 본문**

```json
{
  "machineId": "DESKTOP-ABC123",
  "processName": "steam",
  "action": "add"
}
```

| 필드 | 타입 | 설명 |
|:---|:---|:---|
| `machineId` | string | 대상 머신 ID |
| `processName` | string | 예외로 등록/해제할 프로세스 이름 (확장자 `.exe` 없이) |
| `action` | string | `"add"` 또는 `"remove"` |

**응답 본문** — `GET`과 동일한 `ExceptionsResponse` 형식

```json
{
  "machineId": "DESKTOP-ABC123",
  "exceptions": ["steam", "discord"]
}
```

> `analyze`가 반환하는 `killList`는 서버가 이 예외 목록을 **이미 제외한 뒤** 내려주는 것을 전제로 합니다.

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
