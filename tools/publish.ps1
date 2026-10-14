# Publie se7en DualSense en un seul exécutable autonome (Windows x64, sans .NET à installer).
# Usage : powershell -ExecutionPolicy Bypass -File tools/publish.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish"

if (-not (Test-Path (Join-Path $root "src/Se7en.DualSense.App/Assets/Model/dualsense.s7m"))) {
    Write-Warning "Modèle 3D absent (Assets/Model) : l'accueil utilisera le rendu 2D. Voir tools/controller-3d."
}

dotnet publish (Join-Path $root "src/Se7en.DualSense.App") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw "Échec de la publication" }

$exe = Join-Path $out "se7en DualSense.exe"
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash
$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Exécutable : $exe ($size Mo)"
Write-Host "SHA-256    : $hash"
Write-Host "(à copier dans les notes de la publication GitHub pour la vérification des mises à jour)"
