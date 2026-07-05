param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-network-target-scanner-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-network-target-scanner-test.exe"
@"
using System;
using System.Linq;
using System.Net;

namespace MiSTerCast
{
    static class NetworkTargetScannerTest
    {
        static int Main()
        {
            if (NetworkTargetScanner.FormatDisplayText("MiSTer", IPAddress.Parse("192.168.1.247")) != "MiSTer (192.168.1.247)")
                return Fail("hostname display should include IP address");
            if (NetworkTargetScanner.FormatDisplayText("", IPAddress.Parse("192.168.1.247")) != "192.168.1.247")
                return Fail("nameless display should be the IP address");
            if (NetworkTargetScanner.ExtractTargetValue("MiSTer (192.168.1.247)") != "192.168.1.247")
                return Fail("selection should deploy to the IP in parentheses");
            if (NetworkTargetScanner.ExtractTargetValue("MiSTer") != "MiSTer")
                return Fail("manual hostnames should remain valid");

            var addresses = NetworkTargetScanner.BuildScanAddresses(
                IPAddress.Parse("192.168.1.50"),
                IPAddress.Parse("255.255.255.0"),
                512).Select(a => a.ToString()).ToArray();
            if (!addresses.Contains("192.168.1.1") || !addresses.Contains("192.168.1.254"))
                return Fail("/24 scan should include usable host addresses");
            if (addresses.Contains("192.168.1.0") || addresses.Contains("192.168.1.255") || addresses.Contains("192.168.1.50"))
                return Fail("/24 scan should exclude network, broadcast, and local address");

            var largeSubnetAddresses = NetworkTargetScanner.BuildScanAddresses(
                IPAddress.Parse("10.1.2.50"),
                IPAddress.Parse("255.255.0.0"),
                512).Select(a => a.ToString()).ToArray();
            if (!largeSubnetAddresses.Contains("10.1.2.1") || !largeSubnetAddresses.Contains("10.1.2.254"))
                return Fail("large subnet scan should fall back to the local /24");
            if (largeSubnetAddresses.Contains("10.1.3.1"))
                return Fail("large subnet scan should not scan the full large network");

            string nbtstatOutput =
                "           NetBIOS Remote Machine Name Table\n\n" +
                "       Name               Type         Status\n" +
                "    ---------------------------------------------\n" +
                "    MISTERCADE     <00>  UNIQUE      Registered\n" +
                "    WORKGROUP      <00>  GROUP       Registered\n";
            if (NetworkTargetScanner.ParseNetBiosName(nbtstatOutput) != "MISTERCADE")
                return Fail("NetBIOS parser should read the unique host name");

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
    (Join-Path $repoRoot "FrontEnd\NetworkTargetScanner.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
