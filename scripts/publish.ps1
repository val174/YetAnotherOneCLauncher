# Публикация лаунчера одним файлом, без установленного .NET (ReadyToRun, сборки сжаты):
#   artifacts/publish/win-x64/YetAnotherOneCLauncher.exe — Windows x64
#   artifacts/publish/linux-x64/YetAnotherOneCLauncher   — Linux x64
# Запуск из корня репозитория: powershell -File scripts/publish.ps1 [win-x64] [linux-x64] [-Version 0.2.0]
# -Version задаёт версию программы (иначе — <Version> из Directory.Build.props); релизы собираются с версией из тега.
param(
    [string[]] $Profiles = @('win-x64', 'linux-x64'),
    [string] $Version = ''
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\YetAnotherOneCLauncher.App'
$versionArgs = @()
if ($Version) { $versionArgs = @("-p:Version=$Version") }

foreach ($profile in $Profiles) {
    Write-Host "== $profile"
    dotnet publish $project -p:PublishProfile=$profile @versionArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Публикация $profile не удалась (код $LASTEXITCODE). Если файл занят — закройте лаунчер."
    }
}

foreach ($profile in $Profiles) {
    $name = if ($profile -like 'win-*') { 'YetAnotherOneCLauncher.exe' } else { 'YetAnotherOneCLauncher' }
    $file = Join-Path $PSScriptRoot "..\artifacts\publish\$profile\$name"
    '{0,-10} {1,6:N1} МБ  {2}' -f $profile, ((Get-Item $file).Length / 1MB), (Resolve-Path $file)
}
