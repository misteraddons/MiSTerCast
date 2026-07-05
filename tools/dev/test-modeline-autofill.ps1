param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-modeline-autofill-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-modeline-autofill-test.exe"
@"
using System;

namespace MiSTerCast
{
    static class ModelineAutofillTest
    {
        static int Main()
        {
            Modeline template = new Modeline
            {
                name = "320x240 NTSC",
                pclock = 6.700,
                hactive = 320,
                hbegin = 336,
                hend = 367,
                htotal = 426,
                vactive = 240,
                vbegin = 244,
                vend = 247,
                vtotal = 262,
                interlace = false
            };

            Modeline filled;
            string error;
            if (!ModelineAutofill.TryBuild(template, 384, 224, false, out filled, out error))
                return Fail("autofill should create a modeline: " + error);
            if (filled.hactive != 384 || filled.hbegin != 400 || filled.hend != 431 || filled.htotal != 490)
                return Fail("autofill should preserve horizontal porch/sync widths");
            if (filled.vactive != 224 || filled.vbegin != 228 || filled.vend != 231 || filled.vtotal != 246)
                return Fail("autofill should preserve vertical porch/sync widths");
            if (!ModelineValidator.TryValidate(filled, out error))
                return Fail("autofilled modeline should validate: " + error);

            double templateRefresh = ModelineAutofill.GetRefreshHz(template);
            double filledRefresh = ModelineAutofill.GetRefreshHz(filled);
            if (Math.Abs(templateRefresh - filledRefresh) > 0.01)
                return Fail("autofill should preserve template refresh");

            if (CaptureQuickActions.GetCropModeIndex(CaptureQuickAction.Native) != 1 ||
                CaptureQuickActions.GetCropModeIndex(CaptureQuickAction.Scale2) != 2 ||
                CaptureQuickActions.GetCropModeIndex(CaptureQuickAction.Scale3) != 3 ||
                CaptureQuickActions.GetCropModeIndex(CaptureQuickAction.Full43) != 6 ||
                CaptureQuickActions.GetCropModeIndex(CaptureQuickAction.Full54) != 7)
                return Fail("capture quick actions should map to existing crop modes");

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
    (Join-Path $repoRoot "FrontEnd\Modeline.cs") `
    (Join-Path $repoRoot "FrontEnd\ModelineValidator.cs") `
    (Join-Path $repoRoot "FrontEnd\ModelineAutofill.cs") `
    (Join-Path $repoRoot "FrontEnd\CaptureQuickActions.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
