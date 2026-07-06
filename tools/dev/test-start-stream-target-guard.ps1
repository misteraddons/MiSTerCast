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

$reuseStart = $source.IndexOf("private async Task<bool> UseRunningOrLaunchExistingAsync", [StringComparison]::Ordinal)
if ($reuseStart -lt 0) {
    throw "UseRunningOrLaunchExistingAsync was not found."
}

$reuseEnd = $source.IndexOf("private async Task<bool> WaitForGroovyAfterLaunchAsync", $reuseStart, [StringComparison]::Ordinal)
if ($reuseEnd -lt 0) {
    throw "WaitForGroovyAfterLaunchAsync marker was not found."
}

$reuseMethod = $source.Substring($reuseStart, $reuseEnd - $reuseStart)
$probeIndex = $reuseMethod.IndexOf("GroovyMisterProbe.ProbeAsync(config.Target", [StringComparison]::Ordinal)
$launchIndex = $reuseMethod.IndexOf("deployer.LaunchExistingAsync", [StringComparison]::Ordinal)
if ($probeIndex -lt 0 -or $launchIndex -lt 0 -or $probeIndex -gt $launchIndex) {
    throw "Installed Groovy launch path must probe before relaunching the core."
}

if (-not $reuseMethod.Contains("Groovy_MiSTer already running")) {
    throw "Installed Groovy launch path must report when it reuses an already-running core."
}
