# =====================================================================
#  Ulanzi Studio (D200) 5x2 UPM Monitor 프리셋 자동 등록 스크립트
#  - 설치된 사용자의 %APPDATA% 경로에 맞춰 아이콘 경로 자동 매핑
#  - 기존 D200 프로필에 UPM 5x2 페이지가 없으면 새 페이지로 자동 추가
# =====================================================================
$ErrorActionPreference = 'SilentlyContinue'

$ulanziRoot = Join-Path $env:APPDATA "Ulanzi\UlanziDeck"
$pluginDir  = Join-Path $ulanziRoot "Plugins\com.ulanzi.ulanzistudio.upmmonitor.ulanziPlugin"
$profilesV2 = Join-Path $ulanziRoot "ProfilesV2"

if (-not (Test-Path $profilesV2)) {
    exit 0
}

$baseImg = ($pluginDir -replace '\\', '/') + "/Images"
$prevIcon = ($ulanziRoot -replace '\\', '/') + "/System/Plugins/com.ulanzi.deck.page/Images/btn_previousPage.png"
$upmPageId = "ca524fc8-cb0b-45ca-bf2b-51f1e387c952"

function Build-UpmPageManifestJson {
    $obj = [ordered]@{
        Controllers = @(
            [ordered]@{
                Actions = [ordered]@{}
                Type    = "Encoder"
            },
            [ordered]@{
                Actions = [ordered]@{
                    "0_0" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.app"
                        ActionID    = "c6c135a5-a3e1-44a9-8fd1-a9674d3be804"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "UPM 앱 열기"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/app.png"; IconRel = ""; Name = "UPM" } )
                    }
                    "0_1" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.boost"
                        ActionID    = "06d69916-c27d-49a7-afed-77b3f08a0761"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "빠른 최적화"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/boost.png"; IconRel = ""; Name = "Boost" } )
                    }
                    "0_2" = [ordered]@{
                        Action      = "com.ulanzi.ulanzideck.page.prev"
                        ActionID    = "4302c487-b5ed-48b8-a3a8-804063f8b96a"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "이전 페이지"
                        Plugin      = [ordered]@{ Name = "페이지"; UUID = "com.ulanzi.deck.page"; Version = "1.0" }
                        ViewParam   = @( [ordered]@{ Icon = $prevIcon; IconRel = "" } )
                    }
                    "1_0" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.cpuicon"
                        ActionID    = "450bee97-c389-4b00-8334-df8d334ccf2e"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "CPU 아이콘"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/cpuicon.png"; IconRel = ""; Name = "CPU" } )
                    }
                    "1_1" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.cpuval"
                        ActionID    = "67022cb3-3366-4450-a6ae-e723d4efae17"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "CPU 사용률 수치"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/cpuval.png"; IconRel = ""; Name = "CPU %" } )
                    }
                    "2_0" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.ramicon"
                        ActionID    = "e6f39940-38ea-4a85-8a58-54ec3f4d59a9"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "RAM 아이콘"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/ramicon.png"; IconRel = ""; Name = "RAM" } )
                    }
                    "2_1" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.ramval"
                        ActionID    = "fe79298d-3c5e-429a-8301-b68a9597fdf4"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "RAM 사용률 수치"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/ramval.png"; IconRel = ""; Name = "RAM %" } )
                    }
                    "3_0" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.gpuicon"
                        ActionID    = "795a984d-9d2c-497a-b42a-0ba46e93b8db"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "GPU 아이콘"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/gpuicon.png"; IconRel = ""; Name = "GPU" } )
                    }
                    "3_1" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.gpuval"
                        ActionID    = "dcac30a5-709e-4f8c-844b-d42d8c8c88f3"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "GPU 사용률 수치"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/gpuval.png"; IconRel = ""; Name = "GPU %" } )
                    }
                    "3_2" = [ordered]@{
                        Action      = "com.ulanzi.ulanzideck.smallwindow.window"
                        ActionID    = "e28b9857-c0c2-49b3-8e16-cd50defa0e14"
                        ActionParam = [ordered]@{ SmallViewMode = 200 }
                        LinkedTitle = $true
                        Name        = "배경 설정"
                        Plugin      = [ordered]@{}
                        ViewParam   = @( [ordered]@{ Icon = ""; IconRel = "" } )
                    }
                    "4_0" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.tempicon"
                        ActionID    = "804018d5-8aa9-4fe1-ac85-7d9da0ba89f5"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "온도 아이콘"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/tempicon.png"; IconRel = ""; Name = "TEMP" } )
                    }
                    "4_1" = [ordered]@{
                        Action      = "com.ulanzi.ulanzistudio.upmmonitor.tempval"
                        ActionID    = "97531440-6f4e-4aed-a2f1-058c3b892826"
                        ActionParam = [ordered]@{}
                        LinkedTitle = $true
                        Name        = "GPU 온도 수치"
                        Plugin      = [ordered]@{ Name = "UPM Monitor"; UUID = "com.ulanzi.ulanzistudio.upmmonitor"; Version = "1.0.0" }
                        ViewParam   = @( [ordered]@{ Icon = "$baseImg/tempval.png"; IconRel = ""; Name = "GPU °C" } )
                    }
                }
                Type = "Keypad"
            }
        )
        Icon = ""
        Name = ""
    }
    return ($obj | ConvertTo-Json -Depth 12 -Compress)
}

$pageJson = Build-UpmPageManifestJson
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$profileDirs = Get-ChildItem -Path $profilesV2 -Directory -Filter "*.ulanziProfile"
foreach ($pDir in $profileDirs) {
    $topManifestPath = Join-Path $pDir.FullName "manifest.json"
    if (-not (Test-Path $topManifestPath)) { continue }

    $topJson = Get-Content $topManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $topJson -or -not $topJson.Pages -or -not $topJson.Pages.Pages) { continue }

    $pagesRoot = Join-Path $pDir.FullName "Profiles"
    if (-not (Test-Path $pagesRoot)) {
        New-Item -ItemType Directory -Force $pagesRoot | Out-Null
    }

    # 이미 UPM 액션이 들어있는 페이지가 있는지 확인
    $existingPageManifest = $null
    $pageFolders = Get-ChildItem -Path $pagesRoot -Directory
    foreach ($pf in $pageFolders) {
        $mPath = Join-Path $pf.FullName "manifest.json"
        if (Test-Path $mPath) {
            $raw = Get-Content $mPath -Raw -Encoding UTF8
            if ($raw -match "com\.ulanzi\.ulanzistudio\.upmmonitor") {
                $existingPageManifest = $mPath
                break
            }
        }
    }

    if ($existingPageManifest) {
        # 기존 페이지의 아이콘 경로를 현재 PC 사용자 경로로 최신화
        [System.IO.File]::WriteAllText($existingPageManifest, $pageJson, $utf8NoBom)
    }
    else {
        # 새 페이지 폴더 생성 후 프로필 Pages 배열에 추가
        $newPageDir = Join-Path $pagesRoot $upmPageId
        New-Item -ItemType Directory -Force $newPageDir | Out-Null
        $newPageManifest = Join-Path $newPageDir "manifest.json"
        [System.IO.File]::WriteAllText($newPageManifest, $pageJson, $utf8NoBom)

        $pagesList = @($topJson.Pages.Pages)
        if ($pagesList -notcontains $upmPageId) {
            $topJson.Pages.Pages = $pagesList + $upmPageId
            $topOut = $topJson | ConvertTo-Json -Depth 10 -Compress
            [System.IO.File]::WriteAllText($topManifestPath, $topOut, $utf8NoBom)
        }
    }
}

