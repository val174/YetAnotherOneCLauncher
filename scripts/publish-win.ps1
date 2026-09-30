# Публикация обоих вариантов для Windows x64:
#   artifacts/publish/win-x64/YetAnotherOneCLauncher.exe          — быстрое открытие (ReadyToRun, без сжатия, ~144 МБ)
#   artifacts/publish/win-x64-compact/YetAnotherOneCLauncher.exe  — компактный (ReadyToRun со сжатием, ~63 МБ)
# Запуск из корня репозитория: powershell -File scripts/publish-win.ps1
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\YetAnotherOneCLauncher.App'

foreach ($profile in 'win-x64', 'win-x64-compact') {
    Write-Host "== $profile"
    dotnet publish $project -p:PublishProfile=$profile
    if ($LASTEXITCODE -ne 0) {
        throw "Публикация $profile не удалась (код $LASTEXITCODE). Если файл занят — закройте лаунчер."
    }
}

foreach ($profile in 'win-x64', 'win-x64-compact') {
    $exe = Join-Path $PSScriptRoot "..\artifacts\publish\$profile\YetAnotherOneCLauncher.exe"
    '{0,-16} {1,6:N1} МБ  {2}' -f $profile, ((Get-Item $exe).Length / 1MB), (Resolve-Path $exe)
}
