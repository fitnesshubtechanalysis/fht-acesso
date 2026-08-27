# Gera pacote self-contained para instalação na academia (sem código-fonte).
# Uso: .\scripts\publish-academia.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root "publish\win-x64"

Write-Host "Publicando FHT Acesso para $out ..."

dotnet publish (Join-Path $root "src\FHT.Access.App\FHT.Access.App.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o $out

if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $out "FHT.Access.App.exe"
if (-not (Test-Path $exe)) {
    Write-Error "FHT.Access.App.exe não encontrado em $out"
    exit 1
}

$sizeMb = [math]::Round((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host ""
Write-Host "OK: $exe"
Write-Host "Tamanho total: ${sizeMb} MB"
Write-Host ""
Write-Host "Próximo passo: compacte publish\win-x64 e leve para C:\FHT\Access\ na academia."
Write-Host "Instruções: docs\INSTALACAO_PILOTO.md"
