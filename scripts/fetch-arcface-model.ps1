$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$models = Join-Path $root "src\FHT.Access.Face\models"
New-Item -ItemType Directory -Force -Path $models | Out-Null

function Get-Model($name, $url, $minBytes) {
    $dest = Join-Path $models $name
    if ((Test-Path $dest) -and (Get-Item $dest).Length -ge $minBytes) {
        Write-Host "ok $name"
        return
    }
    Write-Host "baixando $name"
    curl.exe -L --fail --retry 3 -o $dest $url
    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -lt $minBytes) {
        throw "Download incompleto: $name"
    }
}

Get-Model "arcfaceresnet100-8.onnx" `
    "https://huggingface.co/onnxmodelzoo/arcfaceresnet100-8/resolve/main/arcfaceresnet100-8.onnx" `
    20000000
