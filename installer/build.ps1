# Build complet de l'installeur : publish single-file self-contained puis compilation
# Inno Setup. Sortie : dist\Muralis-Setup-<version>.exe
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot

dotnet publish "$root\src\Muralis\Muralis.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Error "Inno Setup 6 introuvable — installer via : winget install -e --id JRSoftware.InnoSetup"
    exit 1
}

& $iscc "$PSScriptRoot\Muralis.iss"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-ChildItem "$root\dist\*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 FullName, @{n = "Taille (Mo)"; e = { [Math]::Round($_.Length / 1MB, 1) } }
