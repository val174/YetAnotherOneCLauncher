# Публикация лаунчера одним файлом, без установленного .NET (ReadyToRun, сборки сжаты):
#   artifacts/publish/win-x64/YetAnotherOneCLauncher.exe — Windows x64
#   artifacts/publish/linux-x64/YetAnotherOneCLauncher   — Linux x64
# Запуск из корня репозитория: powershell -File scripts/publish.ps1 [win-x64] [linux-x64]
param([string[]] $Profiles = @('win-x64', 'linux-x64'))
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\YetAnotherOneCLauncher.App'

foreach ($profile in $Profiles) {
    Write-Host "== $profile"
    dotnet publish $project -p:PublishProfile=$profile
    if ($LASTEXITCODE -ne 0) {
        throw "Публикация $profile не удалась (код $LASTEXITCODE). Если файл занят — закройте лаунчер."
    }
}

foreach ($profile in $Profiles) {
    $name = if ($profile -like 'win-*') { 'YetAnotherOneCLauncher.exe' } else { 'YetAnotherOneCLauncher' }
    $file = Join-Path $PSScriptRoot "..\artifacts\publish\$profile\$name"
    '{0,-10} {1,6:N1} МБ  {2}' -f $profile, ((Get-Item $file).Length / 1MB), (Resolve-Path $file)
}
