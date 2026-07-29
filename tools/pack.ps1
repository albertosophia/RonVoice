# Monta o portable e o zip. Existe para o LEIA-ME e o data\ nao ficarem so'
# numa pasta ignorada pelo git: quem regerar a pasta na mao esquece um deles,
# e o usuario recebe um zip que fala de um arquivo que nao esta la'.
#
#     .\tools\pack.ps1                 -> dist-app\ e RonVoice-app.zip
#     .\tools\pack.ps1 -WithModels     -> inclui data\models (zip fica grande)

param(
    [switch]$WithModels,
    [string]$Output = 'dist-app',
    [string]$Zip = 'RonVoice-app.zip'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    # O dotnet do PATH e' runtime-only nesta maquina; o SDK esta no perfil.
    $dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

    if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

    foreach ($proj in @('RonVoice.App', 'RonVoice.Cli')) {
        & $dotnet publish "$proj\$proj.csproj" -c Release -r win-x64 `
            --self-contained false -o $Output --nologo
        if ($LASTEXITCODE -ne 0) { throw "publish de $proj falhou" }
    }

    New-Item -ItemType Directory -Force (Join-Path $Output 'data') | Out-Null
    Copy-Item 'data\ron_commands.json' (Join-Path $Output 'data') -Force
    Copy-Item 'docs\LEIA-ME.txt' $Output -Force
    Copy-Item 'tools\fetch-models.ps1' $Output -Force

    if ($WithModels) {
        if (-not (Test-Path 'data\models')) { throw 'data\models nao existe; rode fetch-models.ps1' }
        Copy-Item 'data\models' (Join-Path $Output 'data') -Recurse -Force
    }

    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    Compress-Archive -Path "$Output\*" -DestinationPath $Zip

    $mb = [math]::Round((Get-Item $Zip).Length / 1MB, 1)
    Write-Host "$Zip pronto ($mb MB)"
}
finally { Pop-Location }
