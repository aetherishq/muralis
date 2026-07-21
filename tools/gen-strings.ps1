# Régénère src/Muralis/Resources/Strings.Designer.cs depuis Strings.resx (langue neutre).
# À relancer après tout ajout/suppression de clé. Remplace le « custom tool » de Visual
# Studio : aucun package ni VS requis, sortie déterministe (clés triées).
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$resxPath = Join-Path $root "src\Muralis\Resources\Strings.resx"
$outPath = Join-Path $root "src\Muralis\Resources\Strings.Designer.cs"

[xml]$resx = Get-Content $resxPath -Encoding UTF8
$names = @($resx.root.data | ForEach-Object { $_.name }) | Sort-Object

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine(@"
//------------------------------------------------------------------------------
// Généré par tools/gen-strings.ps1 depuis Strings.resx — NE PAS ÉDITER À LA MAIN.
//------------------------------------------------------------------------------
using System.Resources;

namespace Muralis.Resources;

/// <summary>Accès typé aux chaînes localisées (Strings.resx = anglais neutre, satellites par culture).</summary>
public static class Strings
{
    /// <summary>ResourceManager partagé (culture résolue via CurrentUICulture).</summary>
    public static ResourceManager ResourceManager { get; } = new("Muralis.Resources.Strings", typeof(Strings).Assembly);

    private static string Get(string name) => ResourceManager.GetString(name) ?? name;
"@)

foreach ($name in $names) {
    [void]$sb.AppendLine("    public static string $name => Get(nameof($name));")
}
[void]$sb.AppendLine("}")

[System.IO.File]::WriteAllText($outPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "OK $outPath ($($names.Count) clés)"
