param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-modeline-settings-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-modeline-settings-test.exe"
@"
using System;
using System.IO;

namespace MiSTerCast
{
    static class ModelineSettingsTest
    {
        static int Main()
        {
            Modeline valid = new Modeline
            {
                name = "valid",
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

            string error;
            if (!ModelineValidator.TryValidate(valid, out error))
                return Fail("valid modeline was rejected: " + error);

            Modeline badHorizontal = valid;
            badHorizontal.hbegin = 319;
            if (ModelineValidator.TryValidate(badHorizontal, out error) || !error.Contains("Horizontal"))
                return Fail("invalid horizontal timing should be rejected");

            Modeline badClock = valid;
            badClock.pclock = 0;
            if (ModelineValidator.TryValidate(badClock, out error) || !error.Contains("pixel clock"))
                return Fail("zero pixel clock should be rejected");

            MiSTerCastSettings settings = new MiSTerCastSettings
            {
                Target = "MiSTer",
                ModelinePresetIndex = 1,
                PclockText = "6.700",
                HactiveText = "320",
                HbeginText = "336",
                HendText = "367",
                HtotalText = "426",
                VactiveText = "240",
                VbeginText = "244",
                VendText = "247",
                VtotalText = "262",
                Interlaced = false,
                CaptureSourceIndex = 2,
                AlignmentIndex = 8,
                RotationIndex = 3,
                AudioEnabled = true,
                PreviewEnabled = false,
                CropIndex = 7,
                CaptureWidthText = "1280",
                CaptureHeightText = "960",
                CaptureXOffsetText = "-4",
                CaptureYOffsetText = "6"
            };

            StringWriter writer = new StringWriter();
            settings.Save(writer);
            string serialized = writer.ToString();
            if (!serialized.StartsWith(MiSTerCastSettings.CurrentVersion.ToString() + Environment.NewLine))
                return Fail("settings should write the current version");

            MiSTerCastSettings loaded;
            if (!MiSTerCastSettings.TryLoad(new StringReader(serialized), out loaded, out error))
                return Fail("version 2 settings should load: " + error);
            if (loaded.AlignmentIndex != 8 || loaded.PreviewEnabled != false || loaded.CaptureYOffsetText != "6")
                return Fail("version 2 settings did not preserve alignment, preview, and capture offsets");

            string v1 = String.Join(Environment.NewLine, new string[] {
                "1",
                "MiSTer",
                "1",
                "6.700", "320", "336", "367", "426", "240", "244", "247", "262", "0",
                "2", "3", "0", "5",
                "640", "480", "1", "2"
            }) + Environment.NewLine;

            if (!MiSTerCastSettings.TryLoad(new StringReader(v1), out loaded, out error))
                return Fail("version 1 settings should still load: " + error);
            if (loaded.AlignmentIndex != 0 || loaded.PreviewEnabled != true || loaded.RotationIndex != 3 || loaded.AudioEnabled != false || loaded.CropIndex != 5)
                return Fail("version 1 settings should retain old fields and default new fields");

            string baseDir = Path.Combine(Path.GetTempPath(), "mistercast-modeline-base-" + Guid.NewGuid().ToString("N"));
            string currentDir = Path.Combine(Path.GetTempPath(), "mistercast-modeline-current-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);
            Directory.CreateDirectory(currentDir);
            try
            {
                string expectedPath = Path.Combine(baseDir, "modelines.dat");
                File.WriteAllText(expectedPath, "; modelines");
                string actualPath = ModelineFile.ResolvePath(baseDir, currentDir);
                if (!String.Equals(expectedPath, actualPath, StringComparison.OrdinalIgnoreCase))
                    return Fail("modelines.dat should resolve from the executable directory before the current directory");

                string expectedReadmePath = Path.Combine(baseDir, "README.txt");
                File.WriteAllText(expectedReadmePath, "help");
                actualPath = BundledFile.ResolvePath("README.txt", baseDir, currentDir);
                if (!String.Equals(expectedReadmePath, actualPath, StringComparison.OrdinalIgnoreCase))
                    return Fail("README.txt should resolve from the executable directory before the current directory");
            }
            finally
            {
                Directory.Delete(baseDir, true);
                Directory.Delete(currentDir, true);
            }

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
    (Join-Path $repoRoot "FrontEnd\BundledFile.cs") `
    (Join-Path $repoRoot "FrontEnd\ModelineFile.cs") `
    (Join-Path $repoRoot "FrontEnd\ModelineValidator.cs") `
    (Join-Path $repoRoot "FrontEnd\MiSTerCastSettings.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
