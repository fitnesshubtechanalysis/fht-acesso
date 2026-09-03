# Gera pacote Velopack self-contained para instalação na academia.
# Uso local:   .\scripts\publish-academia.ps1
# Uso com tag: .\scripts\publish-academia.ps1 -Version "1.2.3" -Upload
#
# Pré-requisito: `vpk` instalado globalmente
#   dotnet tool install -g vpk
#
# Para releases públicas use a GitHub Action (.github/workflows/release.yml)
# que faz tudo automaticamente em push de tag v*.

param(
    [string]$Version = "",
    [switch]$Upload
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$publishDir = Join-Path $root "publish\win-x64"
$packDir    = Join-Path $root "publish\velopack"

# Descobre versão a partir do csproj se não passada.
if (-not $Version) {
    $csproj = Join-Path $root "src\FHT.Access.App\FHT.Access.App.csproj"
    $xml = [xml](Get-Content $csproj)
    $Version = $xml.Project.PropertyGroup |
        Where-Object { $_.Version } |
        Select-Object -First 1 -ExpandProperty Version
    if (-not $Version) { $Version = "1.0.0" }
}

Write-Host ""
Write-Host "======================================"
Write-Host " FHT Acesso — Publish + Pack"
Write-Host " Versão : $Version"
Write-Host "======================================"
Write-Host ""

# 1. dotnet publish (self-contained win-x64)
Write-Host "[1/3] dotnet publish..."
dotnet publish (Join-Path $root "src\FHT.Access.App\FHT.Access.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) { Write-Error "dotnet publish falhou."; exit $LASTEXITCODE }

$exe = Join-Path $publishDir "FHT.Access.App.exe"
if (-not (Test-Path $exe)) { Write-Error "Executável não encontrado em $publishDir"; exit 1 }

$sizeMb = [math]::Round(
    (Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "   Tamanho: ${sizeMb} MB"

# 2. vpk pack
Write-Host ""
Write-Host "[2/3] vpk pack..."
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    Write-Error "vpk não encontrado. Instale com: dotnet tool install -g vpk"
    exit 1
}

Remove-Item $packDir -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $packDir | Out-Null

vpk pack `
    --packId "FHT.Acesso" `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe "FHT.Access.App.exe" `
    --outputDir $packDir `
    --releaseNotes "FHT Acesso v$Version"

if ($LASTEXITCODE -ne 0) { Write-Error "vpk pack falhou."; exit $LASTEXITCODE }

Write-Host ""
Write-Host "Pacotes Velopack em: $packDir"
Get-ChildItem $packDir | ForEach-Object { Write-Host "  $($_.Name)" }

# 3. Upload para GitHub Release (opcional)
if ($Upload) {
    Write-Host ""
    Write-Host "[3/3] Criando GitHub Release v$Version e fazendo upload..."
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Error "gh CLI não encontrado. Instale de https://cli.github.com"
        exit 1
    }
    $tag = "v$Version"
    gh release create $tag `
        --title "FHT Acesso $tag" `
        --notes "Release automático via publish-academia.ps1" `
        (Join-Path $packDir "*")
    if ($LASTEXITCODE -ne 0) { Write-Error "gh release create falhou."; exit $LASTEXITCODE }
    Write-Host "Release $tag publicado no GitHub."
}

Write-Host ""
Write-Host "OK! Próximo passo:"
Write-Host "  - Para instalar pela primeira vez: execute o Setup .exe do $packDir no totem."
Write-Host "  - Para updates automáticos: o totem busca a versão no endpoint da Gestão."
Write-Host "  - Docs: docs\INSTALACAO.md"
