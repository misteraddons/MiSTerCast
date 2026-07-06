param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$csc = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    throw "csc.exe not found at $csc"
}

$testSource = Join-Path $env:TEMP "mistercast-groovy-release-downloader-test.cs"
$testExe = Join-Path $env:TEMP "mistercast-groovy-release-downloader-test.exe"
@"
using System;

namespace MiSTerCast
{
    static class GroovyReleaseDownloaderTest
    {
        static int Main()
        {
            string json = @"{
              ""tag_name"": ""0.7"",
              ""name"": ""Delta frames"",
              ""published_at"": ""2024-09-22T17:56:45Z"",
              ""assets"": [
                { ""name"": ""retroarch_win64.7z"", ""browser_download_url"": ""https://example.invalid/retroarch.7z"" },
                { ""name"": ""Groovy_20240922.rbf"", ""browser_download_url"": ""https://example.invalid/Groovy_20240922.rbf"" },
                { ""name"": ""MiSTer_groovy"", ""browser_download_url"": ""https://example.invalid/MiSTer_groovy"" },
                { ""name"": ""MiSTer_groovy_XDP"", ""browser_download_url"": ""https://example.invalid/MiSTer_groovy_XDP"" }
              ]
            }";

            var release = GroovyReleaseDownloader.ParseLatestReleaseJson(json, @"C:\cache-root");
            if (release.TagName != "0.7")
                return Fail("tag was not parsed");
            if (release.MisterAssetName != "MiSTer_groovy")
                return Fail("MiSTer_groovy asset was not selected exactly");
            if (release.RbfAssetName != "Groovy_20240922.rbf")
                return Fail("Groovy RBF asset was not selected");
            if (release.MisterBinaryPath != @"C:\cache-root\0.7\MiSTer_groovy")
                return Fail("MiSTer_groovy cache path was wrong: " + release.MisterBinaryPath);
            if (release.GroovyRbfPath != @"C:\cache-root\0.7\Groovy_20240922.rbf")
                return Fail("Groovy RBF cache path was wrong: " + release.GroovyRbfPath);
            if (release.DisplayName != "Delta frames (0.7)")
                return Fail("display name was wrong: " + release.DisplayName);

            bool failed = false;
            try
            {
                GroovyReleaseDownloader.ParseLatestReleaseJson(@"{ ""tag_name"": ""bad"", ""assets"": [] }", @"C:\cache-root");
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }

            if (!failed)
                return Fail("missing release assets should fail clearly");

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

& $csc /nologo /target:exe /out:$testExe /r:System.Runtime.Serialization.dll `
    (Join-Path $repoRoot "FrontEnd\GroovyReleaseDownloader.cs") `
    $testSource
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $testExe
exit $LASTEXITCODE
