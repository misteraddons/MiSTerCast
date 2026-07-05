param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-groovy-target-configurator-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-groovy-target-configurator-test.exe"
@"
using System;

namespace MiSTerCast
{
    static class GroovyTargetConfiguratorTest
    {
        static int Main()
        {
            string inventoryCommand = GroovyTargetConfigurator.BuildInventoryCommand();
            AssertContains(inventoryCommand, "tr '[:upper:]' '[:lower:]'", "inventory command must compare names case-insensitively");
            AssertContains(inventoryCommand, "groovy*.rbf", "inventory command must look for Groovy RBF variants");
            AssertContains(inventoryCommand, "mister_groovy", "inventory command must look for MiSTer_groovy case-insensitively");

            var inventory = GroovyTargetConfigurator.ParseInventoryOutput(
                "MISTER=/media/fat/mister_groovy\nRBF=/media/fat/_Utility/Groovy_20250922.RBF\n");
            if (!inventory.HasMisterBinary || !inventory.HasGroovyRbf)
                return Fail("inventory parser did not detect existing target files");

            var existingPlan = GroovyTargetConfigurator.CreateDeploymentPlan(inventory, false);
            if (existingPlan.UploadMisterBinary)
                return Fail("existing MiSTer_groovy should not be uploaded without force");
            if (existingPlan.UploadGroovyRbf)
                return Fail("existing Groovy RBF should not be uploaded without force");
            if (existingPlan.RemoteGroovyRbfPath != "/media/fat/_Utility/Groovy_20250922.RBF")
                return Fail("existing RBF path should be used for launch");
            if (existingPlan.MisterMainName != "mister_groovy")
                return Fail("existing MiSTer binary basename should be preserved for MiSTer.ini");

            var missingPlan = GroovyTargetConfigurator.CreateDeploymentPlan(new GroovyTargetInventory(), false);
            if (!missingPlan.UploadMisterBinary || !missingPlan.UploadGroovyRbf)
                return Fail("missing target files should be uploaded");
            if (missingPlan.RemoteMisterBinaryPath != "/media/fat/MiSTer_groovy")
                return Fail("missing MiSTer_groovy should deploy to canonical path");
            if (missingPlan.RemoteGroovyRbfPath != "/media/fat/_Utility/Groovy.rbf")
                return Fail("missing Groovy RBF should deploy to canonical path");

            string iniCommand = GroovyTargetConfigurator.BuildEnsureIniCommand("mister_groovy");
            AssertContains(iniCommand, "[Groovy]", "MiSTer.ini command must create the Groovy section");
            AssertContains(iniCommand, "main=", "MiSTer.ini command must set main");
            AssertContains(iniCommand, ".mistercast.bak", "MiSTer.ini command must create a backup");

            return 0;
        }

        static void AssertContains(string haystack, string needle, string message)
        {
            if (haystack.IndexOf(needle, StringComparison.Ordinal) < 0)
                throw new Exception(message + ": missing " + needle);
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
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
