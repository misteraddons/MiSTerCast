param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-status-format-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-status-format-test.exe"
@"
using System;

namespace MiSTerCast
{
    static class StatusFormatTest
    {
        static int Main()
        {
            var stats = new MiSTerCastInterop.StreamStats
            {
                streaming = 1,
                framesSubmitted = 1340,
                fpgaFrame = 1339,
                fpgaVCount = 211,
                fpgaAudio = 0,
                fpgaSynced = 1
            };

            string actual = StreamStatusFormatter.Format(stats);
            string expected = "Status: Streaming | PC frame 1340 | MiSTer synced, 1 frame behind | MiSTer audio off | raster line 211";
            if (actual != expected)
            {
                Console.Error.WriteLine("Expected: " + expected);
                Console.Error.WriteLine("Actual:   " + actual);
                return 1;
            }

            stats.framesSubmitted = 1340;
            stats.fpgaFrame = 1340;
            stats.fpgaVCount = 0;
            stats.fpgaAudio = 1;
            stats.fpgaSynced = 0;

            actual = StreamStatusFormatter.Format(stats);
            expected = "Status: Streaming | PC frame 1340 | MiSTer sync pending, caught up | MiSTer audio on | raster line 0";
            if (actual != expected)
            {
                Console.Error.WriteLine("Expected: " + expected);
                Console.Error.WriteLine("Actual:   " + actual);
                return 1;
            }

            return 0;
        }
    }
}
"@ | Set-Content -LiteralPath $testSource -Encoding UTF8

& $csc /nologo /target:exe /out:$testExe `
    (Join-Path $repoRoot "FrontEnd\MiSTerCastInterop.cs") `
    (Join-Path $repoRoot "FrontEnd\StreamStatusFormatter.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
