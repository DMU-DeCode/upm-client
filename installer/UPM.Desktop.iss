; =====================================================================
;  UPM - Universal PC Manager  설치 스크립트 (Inno Setup 6)
;  선행: build-installer.ps1 이 publish\win-x64 에 자체 포함 퍼블리시 결과를 생성
;  수동 빌드: "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\UPM.Desktop.iss
; =====================================================================

#define MyAppName        "UPM - Universal PC Manager"
#define MyAppShortName   "UPM"
#define MyAppPublisher   "UPM"
#define MyAppExeName     "UPM.Desktop.exe"
#define MyPublishDir     "..\publish\win-x64"
; exe 파일 버전(csproj <Version>)을 그대로 설치 파일 버전으로 사용
#define MyAppVersion     GetVersionNumbersString(MyPublishDir + "\" + MyAppExeName)

[Setup]
; AppId는 업그레이드/제거 식별자이므로 한 번 정하면 절대 바꾸지 마세요.
AppId={{4A9DF3FD-F1EA-422D-AEA3-D9D4701A86C5}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppShortName}
DefaultGroupName={#MyAppShortName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=UPM_Setup_{#MyAppVersion}_x64
SetupIconFile=..\Apps\UPM.Desktop\Assets\upm_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; 앱 자체가 requireAdministrator 이므로 설치도 관리자 권한으로 Program Files 에 설치
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; 업데이트 설치 시 실행 중인 UPM 자동 종료
CloseApplications=force
RestartApplications=no

[Languages]
Name: "korean";  MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "autostart";   Description: "Windows 로그인 시 UPM 자동 실행 (관리자 권한)"; GroupDescription: "추가 옵션:"; Flags: unchecked
Name: "ulanzideck";  Description: "Ulanzi Studio (D200 덱) UPM Monitor 플러그인 및 5x2 프리셋 함께 설치"; GroupDescription: "추가 옵션:"

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\tools\ulanzi-plugin\*"; DestDir: "{userappdata}\Ulanzi\UlanziDeck\Plugins\com.ulanzi.ulanzistudio.upmmonitor.ulanziPlugin"; Flags: ignoreversion recursesubdirs createallsubdirs; Tasks: ulanzideck
Source: "..\tools\ulanzi-plugin\UPM Monitor.ulanziDeckProfile"; DestDir: "{app}"; Flags: ignoreversion; Tasks: ulanzideck
Source: "..\tools\ulanzi-plugin\UPM Monitor.ulanziDeckProfile"; DestDir: "{userdocs}\UPM"; Flags: ignoreversion; Tasks: ulanzideck

[Icons]
Name: "{autoprograms}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppShortName} 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppShortName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 모바일/라즈베리파이 연동용 로컬 제어 서버 포트(TCP 8787) 방화벽 인바운드 허용
Filename: "{sys}\netsh.exe"; \
  Parameters: "advfirewall firewall add rule name=""UPM Mobile Control (8787)"" dir=in action=allow protocol=TCP localport=8787"; \
  Flags: runhidden
; Ulanzi Studio D200 프로필에 5x2 UPM 프리셋 페이지 자동 등록
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{userappdata}\Ulanzi\UlanziDeck\Plugins\com.ulanzi.ulanzistudio.upmmonitor.ulanziPlugin\setup-preset.ps1"""; \
  Flags: runhidden; Tasks: ulanzideck
; requireAdministrator 앱은 레지스트리 Run 키로는 로그인 시 UAC에 막혀 실행되지 않으므로
; '가장 높은 권한' 작업 스케줄러로 자동 실행 등록
Filename: "{sys}\schtasks.exe"; \
  Parameters: "/Create /F /TN ""UPM\AutoStart"" /TR """"""{app}\{#MyAppExeName}"""""" /SC ONLOGON /RL HIGHEST /DELAY 0000:15"; \
  Flags: runhidden; Tasks: autostart
; 설치 완료 후 실행: shellexec 로 실행해야 UAC 승격이 정상 동작 (CreateProcess 시 오류 740)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppShortName}}"; \
  Flags: nowait postinstall skipifsilent shellexec

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillUPM"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""UPM\AutoStart"""; Flags: runhidden; RunOnceId: "DelAutoStart"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""UPM Mobile Control (8787)"""; Flags: runhidden; RunOnceId: "DelFirewall8787"

[Code]
// 'autostart' 미선택으로 재설치/업데이트한 경우 기존 자동 실행 작업 제거
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('autostart')) then
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN "UPM\AutoStart"', '',
         SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
