param([string]$Destination = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
$refs = @('/r:System.dll','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.Web.dll','/r:System.Net.Http.dll','/r:System.Security.dll')
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
foreach ($reference in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')) { $refs += "/r:$(Join-Path $wpf $reference)" }
$icon = Join-Path $PSScriptRoot 'assets\app.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw '앱 아이콘 파일 없음' }
$iconArgs = @("/win32icon:$icon", "/resource:$icon,CodexRelay.AppIcon.ico")
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 "/win32manifest:$PSScriptRoot\app.manifest" "/out:$Destination\CodexRelay.exe" $iconArgs $refs $sources
if ($LASTEXITCODE -ne 0) { throw '앱 빌드 실패' }
$tests = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warn:4 /main:CodexRelay.Tests "/out:$Destination\CodexRelay.Tests.exe" $iconArgs $refs $sources $tests
if ($LASTEXITCODE -ne 0) { throw '검증 프로그램 빌드 실패' }
Write-Output "빌드 완료: $Destination"
