# Gera pacote Velopack self-contained para instalacao na academia.
#
# Uso (rode o script INTEIRO, nao cole linha a linha):
#   cd c:\Projetos\FHT\fht-acesso
#   .\scripts\publish-academia.ps1
#   .\scripts\publish-academia.ps1 -Version "1.0.1"
#   .\scripts\publish-academia.ps1 -Version "1.0.1" -Upload
#
# Pre-requisito: vpk instalado globalmente
#   dotnet tool install -g vpk
#
# Para releases publicas use a GitHub Action (.github/workflows/release.yml)
# que faz tudo automaticamente em push de tag v*.

param(
    [string]$Version = "",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"

# Resolve a raiz do repo mesmo se o script for chamado de outro diretorio.
$scriptDir = if ($PSScriptRoot) {
    $PSScriptRoot
} elseif ($MyInvocation.MyCommand.Path) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
} else {
    $null
}

if (-not $scriptDir) {
    Write-Error @"
Nao foi possivel localizar a pasta do script.

Rode assim (script inteiro, nao cole linha a linha):

  cd c:\Projetos\FHT\fht-acesso
  .\scripts\publish-academia.ps1

"@
    exit 1
}

$root = Split-Path -Parent $scriptDir
$csproj = Join-Path $root "src\FHT.Access.App\FHT.Access.App.csproj"

if (-not (Test-Path $csproj)) {
    Write-Error @"
Projeto nao encontrado em:
  $csproj

Voce esta na pasta errada ou colou trechos do script no PowerShell
(em vez de executar o .ps1). Faca:

  cd c:\Projetos\FHT\fht-acesso
  .\scripts\publish-academia.ps1

"@
    exit 1
}

$publishDir = Join-Path $root "publish\win-x64"
$packDir    = Join-Path $root "publish\velopack"

# Descobre versao a partir do csproj se nao passada.
if (-not $Version) {
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup |
        Where-Object { $_.Version } |
        Select-Object -First 1 -ExpandProperty Version
    if (-not $Version) { $Version = "1.0.0" }
}

Write-Host ""
Write-Host "======================================"
Write-Host " FHT Acesso - Publish + Pack"
Write-Host " Raiz   : $root"
Write-Host " Versao : $Version"
Write-Host "======================================"
Write-Host ""

# 1. dotnet publish (self-contained win-x64)
Write-Host "[1/3] dotnet publish..."
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish falhou."; exit $LASTEXITCODE }

$exe = Join-Path $publishDir "FHT.Access.App.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Executavel nao encontrado em $publishDir"
    exit 1
}

$sizeMb = [math]::Round(
    (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "   Tamanho: ${sizeMb} MB"

# 2. vpk pack
Write-Host ""
Write-Host "[2/3] vpk pack..."
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error "vpk nao encontrado. Instale com: dotnet tool install -g vpk"
    exit 1
}

Remove-Item $packDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $packDir | Out-Null

$notesFile = Join-Path $packDir "RELEASE_NOTES.md"
Set-Content -Path $notesFile -Value "FHT Acesso v$Version" -Encoding UTF8

$assetsDir = Join-Path $root "src\FHT.Access.App\Assets"
$iconPath = Join-Path $assetsDir "app.ico"
$splashPath = Join-Path $assetsDir "installer-splash.png"

$packArgs = @(
    "pack",
    "--packId", "FHT.Acesso",
    "--packTitle", "FHT Acesso",
    "--packVersion", $Version,
    "--packDir", $publishDir,
    "--mainExe", "FHT.Access.App.exe",
    "--outputDir", $packDir,
    "--releaseNotes", $notesFile
)

if (Test-Path $iconPath) {
    $packArgs += @("--icon", $iconPath)
    Write-Host "   Icone : $iconPath"
}
if (Test-Path $splashPath) {
    # Arte completa do instalador (progresso ja vem na imagem).
    # "None" evita a barra padrao do Velopack por cima do design.
    $packArgs += @("--splashImage", $splashPath)
    $packArgs += @("--splashProgressColor", "None")
    Write-Host "   Splash: $splashPath"
}

vpk @packArgs

if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack falhou."; exit $LASTEXITCODE }

Write-Host ""
Write-Host "Pacotes Velopack em: $packDir"
Get-ChildItem $packDir | ForEach-Object { Write-Host "  $($_.Name)" }

# 3. Upload para GitHub Release (opcional)
if ($Upload) {
    Write-Host ""
    Write-Host "[3/3] Criando GitHub Release v$Version e fazendo upload..."
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Error "gh CLI nao encontrado. Instale de https://cli.github.com"
        exit 1
    }
    $tag = "v$Version"
    gh release create $tag `
        --title "FHT Acesso $tag" `
        --notes "Release automatico via publish-academia.ps1" `
        (Join-Path $packDir "*")
    if ($LASTEXITCODE -ne 0) { Write-Error "gh release create falhou."; exit $LASTEXITCODE }
    Write-Host "Release $tag publicado no GitHub."
}

Write-Host ""
Write-Host "OK! Proximo passo:"
Write-Host "  - Para instalar pela primeira vez: execute o Setup .exe do $packDir no totem."
Write-Host "  - Para updates automaticos: o totem busca a versao no endpoint da Gestao."
Write-Host "  - Docs: docs\INSTALACAO.md"