param([string]$Dest = "$PSScriptRoot\..\data\models")

$models = @{
  "en" = @{ Name = "vosk-model-small-en-us-0.15"; Size = 41205931 }
  "pt" = @{ Name = "vosk-model-small-pt-0.3";     Size = 32453112 }
}

New-Item -ItemType Directory -Force -Path $Dest | Out-Null

foreach ($lang in $models.Keys) {
  $m = $models[$lang]
  $target = Join-Path $Dest $m.Name
  if (Test-Path $target) { Write-Host "$($m.Name): ja existe"; continue }

  $zip = Join-Path $env:TEMP "$($m.Name).zip"
  $url = "https://alphacephei.com/vosk/models/$($m.Name).zip"
  Write-Host "baixando $($m.Name) ($([math]::Round($m.Size/1MB,1)) MB)..."
  curl.exe -sSL --fail --max-time 900 -o $zip $url
  if ($LASTEXITCODE -ne 0) { throw "falha ao baixar $url" }

  Expand-Archive -Path $zip -DestinationPath $Dest -Force
  Remove-Item $zip -Force
  Write-Host "$($m.Name): ok"
}

Get-ChildItem $Dest -Directory | Select-Object Name
