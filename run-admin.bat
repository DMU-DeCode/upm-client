@echo off
setlocal
cd /d "%~dp0"

:: 관리자 권한 확인
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [안내] 관리자 권한으로 승격하여 실행합니다...
    powershell -Command "Start-Process cmd -ArgumentList '/k cd /d ""%~dp0"" && dotnet run --project Apps\UPM.Desktop\UPM.Desktop.csproj' -Verb RunAs"
    exit /b
)

:: 이미 관리자 권한인 경우 직접 실행
echo [UPM.Desktop] 관리자 권한으로 dotnet run 실행 중...
dotnet run --project Apps\UPM.Desktop\UPM.Desktop.csproj
pause

