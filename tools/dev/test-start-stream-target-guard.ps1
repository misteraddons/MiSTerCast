$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$mainWindowSource = Join-Path $repoRoot "FrontEnd\MainWindow.xaml.cs"
$source = Get-Content -Raw -LiteralPath $mainWindowSource

$methodStart = $source.IndexOf("private async Task StartStreamWithGuardAsync()", [StringComparison]::Ordinal)
if ($methodStart -lt 0) {
    throw "StartStreamWithGuardAsync was not found."
}

$methodEnd = $source.IndexOf("private async Task<bool> EnsureGroovyReadyForStreamingAsync", $methodStart, [StringComparison]::Ordinal)
if ($methodEnd -lt 0) {
    throw "EnsureGroovyReadyForStreamingAsync marker was not found."
}

$method = $source.Substring($methodStart, $methodEnd - $methodStart)
$guardIndex = $method.IndexOf("EnsureGroovyReadyForStreamingAsync(target)", [StringComparison]::Ordinal)
$startIndex = $method.IndexOf("MiSTerCastInterop.StartStream", [StringComparison]::Ordinal)
if ($guardIndex -lt 0 -or $startIndex -lt 0 -or $guardIndex -gt $startIndex) {
    throw "Start Stream must run the target guard before opening the stream."
}

if ($method.Contains("GroovyMisterProbe.ProbeAsync")) {
    throw "Start Stream must not skip the target guard when an old Groovy process answers UDP."
}
