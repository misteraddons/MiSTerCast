$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$mainWindowSource = Join-Path $repoRoot "FrontEnd\MainWindow.xaml.cs"
$source = Get-Content -Raw -LiteralPath $mainWindowSource

if ($source -match "EnablePreviewCheckBox\.IsChecked\s*=\s*false\s*;") {
    throw "Start stream must not force Enable Preview off."
}
