# 🚀 UPM (Universal Performance Monitor)

UPM은 Windows 환경에서 PC의 하드웨어 상태를 실시간으로 모니터링하고, 시스템 리소스를 최적화하며, 해당 데이터를 원격 서버로 전송할 수 있는 종합 성능 관리 도구입니다.

## ✨ 주요 기능

- **📊 실시간 대시보드**: WPF 기반의 시각적인 UI를 통해 CPU, RAM 사용량 및 GPU 온도를 실시간으로 확인.
- **🔍 상세 하드웨어 분석**: CPU 모델명, 코어/스레드 정보, 메인보드 사양 및 총 RAM 용량 자동 감지.
- **⚡ 시스템 부스트**: 메모리를 과도하게 점유하는 불필요한 프로세스를 한 번의 클릭으로 정리하여 시스템 성능 최적화.
- **🌐 서버 동기화**: 수집된 하드웨어 사양 및 실시간 상태 정보를 REST API를 통해 외부 서버로 자동 전송.

## 🛠 기술 스택

- **Runtime**: .NET 8.0 (C#)
- **UI Framework**: WPF (Windows Presentation Foundation)
- **Monitoring**: LibreHardwareMonitor, System.Management (WMI)
- **Communication**: HttpClient (JSON Serialization)
- **Charts**: LiveCharts.Wpf

## 📂 프로젝트 구조

| 폴더 / 프로젝트 | 설명 |
| :--- | :--- |
| **UPM.Desktop** | 메인 WPF 애플리케이션 (UI 및 사용자 인터페이스 제어) |
| **UPM.Core** | 하드웨어 모니터링 로직 및 시스템 최적화(Boost) 기능 포함 |
| **UPM.Communication** | 서버 전송을 위한 API 클라이언트 라이브러리 |
| **UPM.Models** | 프로젝트 전반에서 공유되는 데이터 전송 객체 (DTO) |

## 🚀 시작하기

### 사전 요구 사항
- Windows OS (WMI 및 하드웨어 카운터 접근 권한 필요)
- [.NET 8.0 런타임](https://dotnet.microsoft.com/download/dotnet/8.0)

### 서버 연동 설정
서버로 데이터를 전송하려면 아래 파일에서 서버의 Endpoint 주소를 수정하십시오.
- 파일 위치: `Apps/UPM.Desktop/MainWindow.xaml.cs`
- 수정 코드: `new ApiServerClient("http://your-server-api.com");`

## 📝 라이선스
이 프로젝트는 개인 용도 및 교육 목적으로 작성되었습니다.
