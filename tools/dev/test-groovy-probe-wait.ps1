param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-groovy-probe-wait-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-groovy-probe-wait-test.exe"
@"
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MiSTerCast
{
    static class GroovyProbeWaitTest
    {
        static int Main()
        {
            return Run().GetAwaiter().GetResult();
        }

        static async Task<int> Run()
        {
            int postLaunchTimeout = GroovyMisterProbe.DefaultPostLaunchProbeTimeoutMilliseconds;
            if (postLaunchTimeout < 15000)
                return Fail("post-launch probe timeout should wait long enough for core startup");

            int attempts = 0;
            var logs = new List<string>();
            GroovyMisterProbeResult result = await GroovyMisterProbe.ProbeUntilAsync(
                "MiSTer",
                totalTimeoutMilliseconds: 5000,
                attemptTimeoutMilliseconds: 123,
                retryDelayMilliseconds: 0,
                log: (message, error) => logs.Add(message),
                probeAsync: (target, timeout) =>
                {
                    attempts++;
                    if (target != "MiSTer")
                        throw new Exception("unexpected target " + target);
                    if (timeout != 123)
                        throw new Exception("unexpected attempt timeout " + timeout);
                    if (attempts < 3)
                    {
                        return Task.FromResult(new GroovyMisterProbeResult
                        {
                            Success = false,
                            Address = "192.168.1.247",
                            Port = GroovyMisterProbe.DefaultPort,
                            Message = "no UDP ACK from MiSTer"
                        });
                    }

                    return Task.FromResult(new GroovyMisterProbeResult
                    {
                        Success = true,
                        Address = "192.168.1.247",
                        Port = GroovyMisterProbe.DefaultPort,
                        Message = "Groovy_MiSTer detected"
                    });
                },
                delayAsync: delayMilliseconds => Task.CompletedTask);

            if (!result.Success)
                return Fail("ProbeUntilAsync should return the first successful retry result");
            if (attempts != 3)
                return Fail("ProbeUntilAsync should retry after failed ACK attempts");
            if (!logs.Any(log => log.Contains("retrying UDP probe")))
                return Fail("ProbeUntilAsync should log retry progress");

            return 0;
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
    (Join-Path $repoRoot "FrontEnd\GroovyMisterProbe.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
