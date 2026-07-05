using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiSTerCast
{
    class GroovyTargetInventory
    {
        public string MisterBinaryPath { get; set; }
        public string GroovyRbfPath { get; set; }

        public bool HasMisterBinary
        {
            get { return !String.IsNullOrWhiteSpace(MisterBinaryPath); }
        }

        public bool HasGroovyRbf
        {
            get { return !String.IsNullOrWhiteSpace(GroovyRbfPath); }
        }
    }

    class GroovyTargetDeploymentPlan
    {
        public bool UploadMisterBinary { get; set; }
        public bool UploadGroovyRbf { get; set; }
        public string RemoteMisterBinaryPath { get; set; }
        public string RemoteGroovyRbfPath { get; set; }
        public string MisterMainName { get; set; }
    }

    public class GroovyTargetDeploymentConfig
    {
        public string Target { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string MisterBinaryPath { get; set; }
        public string GroovyRbfPath { get; set; }
        public bool ForceRedeploy { get; set; }
    }

    static class GroovyTargetConfigurator
    {
        public const string DefaultUsername = "root";
        public const string DefaultPassword = "1";
        public const string CanonicalMisterBinaryPath = "/media/fat/MiSTer_groovy";
        public const string CanonicalGroovyRbfPath = "/media/fat/_Utility/Groovy.rbf";

        public static string BuildInventoryCommand()
        {
            return "mister=''; " +
                "for f in /media/fat/*; do [ -f \"$f\" ] || continue; n=$(basename \"$f\" | tr '[:upper:]' '[:lower:]'); if [ \"$n\" = 'mister_groovy' ]; then mister=\"$f\"; break; fi; done; " +
                "rbf=''; " +
                "for f in /media/fat/_Utility/*; do [ -f \"$f\" ] || continue; n=$(basename \"$f\" | tr '[:upper:]' '[:lower:]'); case \"$n\" in groovy*.rbf) rbf=\"$f\"; break;; esac; done; " +
                "printf 'MISTER=%s\nRBF=%s\n' \"$mister\" \"$rbf\"";
        }

        public static GroovyTargetInventory ParseInventoryOutput(string output)
        {
            var inventory = new GroovyTargetInventory();
            if (String.IsNullOrEmpty(output))
                return inventory;

            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.StartsWith("MISTER=", StringComparison.Ordinal))
                    inventory.MisterBinaryPath = line.Substring("MISTER=".Length).Trim();
                else if (line.StartsWith("RBF=", StringComparison.Ordinal))
                    inventory.GroovyRbfPath = line.Substring("RBF=".Length).Trim();
            }

            return inventory;
        }

        public static GroovyTargetDeploymentPlan CreateDeploymentPlan(GroovyTargetInventory inventory, bool forceRedeploy, string deploymentId = null)
        {
            if (inventory == null)
                inventory = new GroovyTargetInventory();

            bool uploadMisterBinary = forceRedeploy || !inventory.HasMisterBinary;
            bool uploadGroovyRbf = forceRedeploy || !inventory.HasGroovyRbf;
            string remoteMisterBinaryPath = uploadMisterBinary
                ? GetUploadMisterBinaryPath(inventory, forceRedeploy, deploymentId)
                : inventory.MisterBinaryPath;
            string remoteGroovyRbfPath = uploadGroovyRbf ? CanonicalGroovyRbfPath : inventory.GroovyRbfPath;
            string misterMainName = RemoteFileName(remoteMisterBinaryPath);
            if (String.IsNullOrWhiteSpace(misterMainName))
                misterMainName = RemoteFileName(CanonicalMisterBinaryPath);

            return new GroovyTargetDeploymentPlan
            {
                UploadMisterBinary = uploadMisterBinary,
                UploadGroovyRbf = uploadGroovyRbf,
                RemoteMisterBinaryPath = remoteMisterBinaryPath,
                RemoteGroovyRbfPath = remoteGroovyRbfPath,
                MisterMainName = misterMainName
            };
        }

        private static string GetUploadMisterBinaryPath(GroovyTargetInventory inventory, bool forceRedeploy, string deploymentId)
        {
            if (!forceRedeploy || inventory == null || !inventory.HasMisterBinary)
                return CanonicalMisterBinaryPath;

            if (String.IsNullOrWhiteSpace(deploymentId))
                deploymentId = DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            return "/media/fat/MiSTer_groovy_mistercast_" + SanitizeRemoteNameFragment(deploymentId);
        }

        private static string SanitizeRemoteNameFragment(string value)
        {
            if (String.IsNullOrWhiteSpace(value))
                return "deploy";

            char[] chars = value
                .Select(c => Char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
                .ToArray();
            string sanitized = new string(chars).Trim('_');
            return String.IsNullOrWhiteSpace(sanitized) ? "deploy" : sanitized;
        }

        public static string BuildEnsureIniCommand(string misterMainName)
        {
            if (String.IsNullOrWhiteSpace(misterMainName))
                misterMainName = RemoteFileName(CanonicalMisterBinaryPath);

            string awkScript =
                "BEGIN{in_g=0;found=0;wrote=0} " +
                "/^\\[Groovy\\]/{if(in_g&&!wrote){print \"main=\" main;wrote=1};in_g=1;found=1;print;next} " +
                "/^\\[.*\\]/{if(in_g&&!wrote){print \"main=\" main;wrote=1};in_g=0;print;next} " +
                "in_g&&/^main=/{if(!wrote){print \"main=\" main;wrote=1};next} " +
                "{print} " +
                "END{if(!found){print \"\";print \"[Groovy]\";print \"main=\" main}else if(in_g&&!wrote){print \"main=\" main}}";

            return "ini=/media/fat/MiSTer.ini; main=" + QuoteRemote(misterMainName) + "; " +
                "[ -f \"$ini\" ] || touch \"$ini\"; " +
                "cp \"$ini\" \"$ini.mistercast.bak\"; " +
                "tmp=/tmp/MiSTer.ini.mistercast.$$; " +
                "awk -v main=\"$main\" '" + awkScript + "' \"$ini\" > \"$tmp\" && cp \"$tmp\" \"$ini\" && rm -f \"$tmp\"";
        }

        public static string BuildLaunchCommand(string remoteGroovyRbfPath)
        {
            if (String.IsNullOrWhiteSpace(remoteGroovyRbfPath))
                remoteGroovyRbfPath = CanonicalGroovyRbfPath;
            return "echo " + QuoteRemote("load_core " + remoteGroovyRbfPath) + " > /dev/MiSTer_cmd";
        }

        public static void ValidateDeploymentConfig(GroovyTargetDeploymentConfig config, GroovyTargetDeploymentPlan plan)
        {
            if (config == null)
                throw new ArgumentNullException("config");
            if (String.IsNullOrWhiteSpace(config.Target))
                throw new InvalidOperationException("Target is required.");
            if (String.IsNullOrWhiteSpace(config.Username))
                throw new InvalidOperationException("Username is required.");
            if (plan == null)
                throw new ArgumentNullException("plan");

            if (plan.UploadMisterBinary)
                ValidateLocalFile(config.MisterBinaryPath, "MiSTer_groovy");
            if (plan.UploadGroovyRbf)
                ValidateLocalFile(config.GroovyRbfPath, "Groovy RBF");
        }

        public static string FindDefaultMisterBinaryPath()
        {
            return FindFirstExistingPath(new[]
            {
                @"..\Groovy_MiSTer\build\hps-src\MiSTer_groovy",
                @"..\Groovy_MiSTer\hps_linux\src\MiSTer_groovy",
                @"..\Groovy_MiSTer\hps_linux\MiSTer_groovy"
            });
        }

        public static string FindDefaultGroovyRbfPath()
        {
            string directMatch = FindFirstExistingPath(new[]
            {
                @"..\Groovy_MiSTer\Groovy.rbf",
                @"..\Groovy_MiSTer\output_files\Groovy.rbf",
                @"..\Groovy_MiSTer\releases\Groovy.rbf"
            });
            if (!String.IsNullOrWhiteSpace(directMatch))
                return directMatch;

            string groovyRoot = ResolveRepoRelativePath(@"..\Groovy_MiSTer");
            if (!Directory.Exists(groovyRoot))
                return "";

            string[] searchRoots =
            {
                groovyRoot,
                Path.Combine(groovyRoot, "output_files"),
                Path.Combine(groovyRoot, "releases"),
                Path.Combine(groovyRoot, "test-builds"),
                Path.Combine(groovyRoot, "old-builds")
            };

            foreach (string searchRoot in searchRoots)
            {
                if (!Directory.Exists(searchRoot))
                    continue;

                string match = Directory.EnumerateFiles(searchRoot, "Groovy*.rbf", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (!String.IsNullOrWhiteSpace(match))
                    return match;
            }

            return "";
        }

        public static string RemoteFileName(string remotePath)
        {
            if (String.IsNullOrWhiteSpace(remotePath))
                return "";

            int slash = remotePath.LastIndexOf('/');
            return slash >= 0 ? remotePath.Substring(slash + 1) : remotePath;
        }

        public static string QuoteRemote(string value)
        {
            if (value == null)
                value = "";
            return "'" + value.Replace("'", "'\"'\"'") + "'";
        }

        private static void ValidateLocalFile(string path, string label)
        {
            if (String.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException(label + " path is required.");
            if (!File.Exists(path))
                throw new FileNotFoundException(label + " was not found.", path);
        }

        private static string FindFirstExistingPath(IEnumerable<string> repoRelativePaths)
        {
            foreach (string repoRelativePath in repoRelativePaths)
            {
                string path = ResolveRepoRelativePath(repoRelativePath);
                if (File.Exists(path))
                    return path;
            }

            return "";
        }

        private static string ResolveRepoRelativePath(string relativePath)
        {
            string appBase = AppDomain.CurrentDomain.BaseDirectory;
            string repoRoot = Path.GetFullPath(Path.Combine(appBase, @"..\..\.."));
            return Path.GetFullPath(Path.Combine(repoRoot, relativePath));
        }
    }
}
