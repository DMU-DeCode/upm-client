# =====================================================================
#  UPM 설치 파일 빌드: 자체 포함 퍼블리시 -> Inno Setup 컴파일
#  사용법 (프로젝트 루트에서):
#    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
#    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -SkipInno   # 퍼블리시만
# =====================================================================
param(
    [switch]$SkipInno
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."
Set-Location $root

$project    = "Apps\UPM.Desktop\UPM.Desktop.csproj"
$publishDir = Join-Path $root "publish\win-x64"

Write-Host "[1/3] 이전 퍼블리시 결과 정리" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "[2/3] 자체 포함 퍼블리시 (win-x64, Release)" -ForegroundColor Cyan
dotnet publish $project -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 실패 (exit $LASTEXITCODE)" }

$exe = Join-Path $publishDir "UPM.Desktop.exe"
if (-not (Test-Path $exe)) { throw "퍼블리시 결과에 UPM.Desktop.exe 가 없습니다: $publishDir" }
Write-Host "      -> $publishDir" -ForegroundColor DarkGray

if ($SkipInno) { Write-Host "Inno Setup 단계 건너뜀" -ForegroundColor Yellow; return }

Write-Host "[3/3] Inno Setup 컴파일" -ForegroundColor Cyan
$iscc = @(
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "ISCC.exe 를 찾을 수 없습니다. Inno Setup 6 설치: winget install JRSoftware.InnoSetup"
}

& $iscc "installer\UPM.Desktop.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 컴파일 실패 (exit $LASTEXITCODE)" }

Write-Host "완료: $(Join-Path $root 'dist')" -ForegroundColor Green

