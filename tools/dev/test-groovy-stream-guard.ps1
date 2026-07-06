param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-groovy-stream-guard-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-groovy-stream-guard-test.exe"
@"
using System;

namespace MiSTerCast
{
    static class GroovyStreamGuardTest
    {
        static int Main()
        {
            var latest = new GroovyReleaseInfo
            {
                TagName = "0.7",
                MisterAssetName = "MiSTer_groovy",
                RbfAssetName = "Groovy_20240922.rbf"
            };

            var sameStatus = InstalledStatus("0.7");
            if (GroovyStreamGuard.DecideRepairAction(sameStatus, latest) != GroovyStreamRepairAction.LaunchExisting)
                return Fail("matching installed release should launch existing core");

            var oldStatus = InstalledStatus("0.6");
            if (GroovyStreamGuard.DecideRepairAction(oldStatus, latest) != GroovyStreamRepairAction.UpdateLatest)
                return Fail("older installed release should offer latest update");

            var unknownStatus = InstalledStatus(null);
            if (GroovyStreamGuard.DecideRepairAction(unknownStatus, latest) != GroovyStreamRepairAction.OfferUpdateOrLaunchExisting)
                return Fail("installed release without marker should offer update or launch existing");

            var missingStatus = new GroovyTargetStatus
            {
                Inventory = new GroovyTargetInventory()
            };
            if (GroovyStreamGuard.DecideRepairAction(missingStatus, latest) != GroovyStreamRepairAction.InstallLatest)
                return Fail("missing target files should offer latest install");

            string markerText = "TAG=0.7\nMISTER=MiSTer_groovy\nRBF=Groovy_20240922.rbf\n";
            GroovyTargetManifest parsed = GroovyTargetManifest.Parse(markerText);
            if (parsed == null || parsed.TagName != "0.7" || parsed.RbfAssetName != "Groovy_20240922.rbf")
                return Fail("manifest parser should read deployed release metadata");
            if (!parsed.MatchesRelease(latest))
                return Fail("manifest should match the corresponding latest release");
            if (!GroovyTargetManifest.BuildReadCommand().Contains(GroovyTargetManifest.RemotePath))
                return Fail("manifest read command should target the remote marker path");

            return 0;
        }

        static GroovyTargetStatus InstalledStatus(string tag)
        {
            return new GroovyTargetStatus
            {
                Inventory = new GroovyTargetInventory
                {
                    MisterBinaryPath = "/media/fat/MiSTer_groovy",
                    GroovyRbfPath = "/media/fat/_Utility/Groovy.rbf"
                },
                Manifest = tag == null ? null : new GroovyTargetManifest
                {
                    TagName = tag,
                    MisterAssetName = "MiSTer_groovy",
                    RbfAssetName = "Groovy_20240922.rbf"
                }
            };
        }

        static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            return 1;
        }
    }
}
"@ | Set-Content -LiteralPath $testSource -Encoding UTF8

& $csc /nologo /target:exe /out:$testExe `
    (Join-Path $repoRoot "FrontEnd\GroovyTargetConfigurator.cs") `
    (Join-Path $repoRoot "FrontEnd\GroovyReleaseDownloader.cs") `
    (Join-Path $repoRoot "FrontEnd\GroovyTargetManifest.cs") `
    (Join-Path $repoRoot "FrontEnd\GroovyStreamGuard.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
