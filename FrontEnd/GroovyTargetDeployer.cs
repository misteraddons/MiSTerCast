using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MiSTerCast
{
    class GroovyTargetDeploymentResult
    {
        public GroovyTargetInventory Inventory { get; set; }
        public GroovyTargetDeploymentPlan Plan { get; set; }
    }

    class GroovyTargetDeployer
    {
        public async Task<GroovyTargetDeploymentResult> ConfigureAndLaunchAsync(
            GroovyTargetDeploymentConfig config,
            Action<string, bool> log)
        {
            if (config == null)
                throw new ArgumentNullException("config");

            string plinkPath = FindTool("plink.exe");
            string pscpPath = FindTool("pscp.exe");
            if (String.IsNullOrWhiteSpace(plinkPath))
                throw new InvalidOperationException("plink.exe was not found. Install PuTTY or add plink.exe to PATH.");
            if (String.IsNullOrWhiteSpace(pscpPath))
                throw new InvalidOperationException("pscp.exe was not found. Install PuTTY or add pscp.exe to PATH.");

            string[] hostKeyIds = await ScanHostKeyIdsAsync(config.Target, log);

            Log(log, "Checking target for existing Groovy files...");
            CommandResult inventoryResult = await RunPlinkAsync(plinkPath, config, GroovyTargetConfigurator.BuildInventoryCommand(), hostKeyIds);
            EnsureSuccess("Target check failed", inventoryResult);
            GroovyTargetInventory inventory = GroovyTargetConfigurator.ParseInventoryOutput(inventoryResult.StandardOutput);
            Log(log, inventory.HasMisterBinary
                ? "Found MiSTer_groovy on target: " + inventory.MisterBinaryPath
                : "MiSTer_groovy not found on target.");
            Log(log, inventory.HasGroovyRbf
                ? "Found Groovy RBF on target: " + inventory.GroovyRbfPath
                : "Groovy RBF not found on target.");

            GroovyTargetDeploymentPlan plan = GroovyTargetConfigurator.CreateDeploymentPlan(inventory, config.ForceRedeploy);
            GroovyTargetConfigurator.ValidateDeploymentConfig(config, plan);

            await RunRequiredPlinkAsync(plinkPath, config, "Preparing target folders", "mkdir -p /media/fat/_Utility", hostKeyIds);

            if (plan.UploadMisterBinary)
            {
                Log(log, "Uploading MiSTer_groovy to " + plan.RemoteMisterBinaryPath + "...");
                CommandResult result = await RunPscpAsync(pscpPath, config, config.MisterBinaryPath, plan.RemoteMisterBinaryPath, hostKeyIds);
                EnsureSuccess("MiSTer_groovy upload failed", result);
            }
            else
            {
                Log(log, "Skipping MiSTer_groovy upload; target already has it.");
            }

            if (plan.UploadGroovyRbf)
            {
                Log(log, "Uploading Groovy RBF...");
                CommandResult result = await RunPscpAsync(pscpPath, config, config.GroovyRbfPath, plan.RemoteGroovyRbfPath, hostKeyIds);
                EnsureSuccess("Groovy RBF upload failed", result);
            }
            else
            {
                Log(log, "Skipping Groovy RBF upload; target already has it.");
            }

            await RunRequiredPlinkAsync(plinkPath, config, "Updating MiSTer.ini", GroovyTargetConfigurator.BuildEnsureIniCommand(plan.MisterMainName), hostKeyIds);
            await RunRequiredPlinkAsync(plinkPath, config, "Syncing target", "chmod +x " + GroovyTargetConfigurator.QuoteRemote(plan.RemoteMisterBinaryPath) + " 2>/dev/null || true; sync", hostKeyIds);
            await RunRequiredPlinkAsync(plinkPath, config, "Launching Groovy core", GroovyTargetConfigurator.BuildLaunchCommand(plan.RemoteGroovyRbfPath), hostKeyIds);

            return new GroovyTargetDeploymentResult
            {
                Inventory = inventory,
                Plan = plan
            };
        }

        private async Task RunRequiredPlinkAsync(string plinkPath, GroovyTargetDeploymentConfig config, string label, string remoteCommand, IEnumerable<string> hostKeyIds = null)
        {
            CommandResult result = await RunPlinkAsync(plinkPath, config, remoteCommand, hostKeyIds);
            EnsureSuccess(label + " failed", result);
        }

        private Task<CommandResult> RunPlinkAsync(string plinkPath, GroovyTargetDeploymentConfig config, string remoteCommand, IEnumerable<string> hostKeyIds)
        {
            return RunProcessAsync(plinkPath, BuildPlinkArguments(config, remoteCommand, hostKeyIds));
        }

        private Task<CommandResult> RunPscpAsync(string pscpPath, GroovyTargetDeploymentConfig config, string localPath, string remotePath, IEnumerable<string> hostKeyIds)
        {
            return RunProcessAsync(pscpPath, BuildPscpArguments(config, localPath, remotePath, hostKeyIds));
        }

        public static string BuildPlinkArguments(GroovyTargetDeploymentConfig config, string remoteCommand, IEnumerable<string> hostKeyIds)
        {
            return "-ssh " + BuildHostKeyArguments(hostKeyIds) +
                "-batch -l " + QuoteArgument(config.Username) +
                " -pw " + QuoteArgument(config.Password ?? "") +
                " " + QuoteArgument(config.Target) +
                " " + QuoteArgument(remoteCommand);
        }

        public static string BuildPscpArguments(GroovyTargetDeploymentConfig config, string localPath, string remotePath, IEnumerable<string> hostKeyIds)
        {
            string remote = config.Username + "@" + config.Target + ":" + remotePath;
            return BuildHostKeyArguments(hostKeyIds) +
                "-batch -scp -pw " + QuoteArgument(config.Password ?? "") +
                " " + QuoteArgument(localPath) +
                " " + QuoteArgument(remote);
        }

        private Task<CommandResult> RunProcessAsync(string fileName, string arguments)
        {
            return Task.Run(() =>
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return new CommandResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = stdout,
                        StandardError = stderr
                    };
                }
            });
        }

        private static void EnsureSuccess(string label, CommandResult result)
        {
            if (result.ExitCode == 0)
                return;

            string detail = (result.StandardError + Environment.NewLine + result.StandardOutput).Trim();
            if (String.IsNullOrWhiteSpace(detail))
                detail = "exit code " + result.ExitCode;
            throw new InvalidOperationException(label + ": " + detail);
        }

        private static void Log(Action<string, bool> log, string message)
        {
            if (log != null)
                log(message, false);
        }

        private async Task<string[]> ScanHostKeyIdsAsync(string target, Action<string, bool> log)
        {
            string sshKeyscanPath = FindTool("ssh-keyscan.exe");
            if (String.IsNullOrWhiteSpace(sshKeyscanPath))
            {
                Log(log, "ssh-keyscan.exe not found; using PuTTY's cached host keys.");
                return new string[0];
            }

            CommandResult result = await RunProcessAsync(sshKeyscanPath, "-T 5 " + QuoteArgument(target));
            string[] hostKeyIds = ParseSshKeyScanHostKeyIds(result.StandardOutput).ToArray();
            if (hostKeyIds.Length > 0)
                Log(log, "Read SSH host key fingerprint from target.");
            else
                Log(log, "Could not read SSH host key fingerprint; using PuTTY's cached host keys.");
            return hostKeyIds;
        }

        public static IEnumerable<string> ParseSshKeyScanHostKeyIds(string keyscanOutput)
        {
            if (String.IsNullOrWhiteSpace(keyscanOutput))
                yield break;

            string[] lines = keyscanOutput.Replace("\r\n", "\n").Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3)
                    continue;

                byte[] keyBlob;
                try
                {
                    keyBlob = Convert.FromBase64String(parts[2]);
                }
                catch (FormatException)
                {
                    continue;
                }

                using (SHA256 sha256 = SHA256.Create())
                {
                    string fingerprint = Convert.ToBase64String(sha256.ComputeHash(keyBlob)).TrimEnd('=');
                    yield return "SHA256:" + fingerprint;
                }
            }
        }

        private static string BuildHostKeyArguments(IEnumerable<string> hostKeyIds)
        {
            if (hostKeyIds == null)
                return "";

            var builder = new StringBuilder();
            foreach (string hostKeyId in hostKeyIds.Where(h => !String.IsNullOrWhiteSpace(h)).Distinct())
            {
                builder.Append("-hostkey ");
                builder.Append(QuoteArgument(hostKeyId));
                builder.Append(' ');
            }
            return builder.ToString();
        }

        private static string FindTool(string toolName)
        {
            string[] commonPaths =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PuTTY", toolName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PuTTY", toolName)
            };

            foreach (string path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string directory in pathVariable.Split(Path.PathSeparator))
            {
                if (String.IsNullOrWhiteSpace(directory))
                    continue;
                string candidate = Path.Combine(directory.Trim(), toolName);
                if (File.Exists(candidate))
                    return candidate;
            }

            return "";
        }

        private static string QuoteArgument(string value)
        {
            if (String.IsNullOrEmpty(value))
                return "\"\"";

            bool needsQuotes = value.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) >= 0;
            if (!needsQuotes)
                return value;

            var builder = new StringBuilder();
            builder.Append('"');
            int backslashes = 0;
            foreach (char c in value)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1);
                    builder.Append('"');
                    backslashes = 0;
                }
                else
                {
                    builder.Append('\\', backslashes);
                    builder.Append(c);
                    backslashes = 0;
                }
            }
            builder.Append('\\', backslashes * 2);
            builder.Append('"');
            return builder.ToString();
        }
    }

    class CommandResult
    {
        public int ExitCode { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
    }
}
