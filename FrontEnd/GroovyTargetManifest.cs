using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MiSTerCast
{
    public class GroovyTargetManifest
    {
        public const string RemoteDirectory = "/media/fat/.mistercast";
        public const string RemotePath = RemoteDirectory + "/groovy-release.txt";

        public string TagName { get; set; }
        public string MisterAssetName { get; set; }
        public string RbfAssetName { get; set; }
        public string MisterSha256 { get; set; }
        public string RbfSha256 { get; set; }

        public static GroovyTargetManifest FromRelease(GroovyReleaseInfo release)
        {
            if (release == null)
                return null;

            return new GroovyTargetManifest
            {
                TagName = release.TagName,
                MisterAssetName = release.MisterAssetName,
                RbfAssetName = release.RbfAssetName,
                MisterSha256 = HashFileIfPresent(release.MisterBinaryPath),
                RbfSha256 = HashFileIfPresent(release.GroovyRbfPath)
            };
        }

        public static GroovyTargetManifest FromLocalFiles(string misterBinaryPath, string groovyRbfPath)
        {
            return new GroovyTargetManifest
            {
                TagName = InferTagName(misterBinaryPath, groovyRbfPath),
                MisterAssetName = Path.GetFileName(misterBinaryPath),
                RbfAssetName = Path.GetFileName(groovyRbfPath),
                MisterSha256 = HashFileIfPresent(misterBinaryPath),
                RbfSha256 = HashFileIfPresent(groovyRbfPath)
            };
        }

        public static GroovyTargetManifest Parse(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
                return null;

            var manifest = new GroovyTargetManifest();
            foreach (string rawLine in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string key = line.Substring(0, equals).Trim();
                string value = line.Substring(equals + 1).Trim();
                if (String.Equals(key, "TAG", StringComparison.OrdinalIgnoreCase))
                    manifest.TagName = value;
                else if (String.Equals(key, "MISTER", StringComparison.OrdinalIgnoreCase))
                    manifest.MisterAssetName = value;
                else if (String.Equals(key, "RBF", StringComparison.OrdinalIgnoreCase))
                    manifest.RbfAssetName = value;
                else if (String.Equals(key, "MISTER_SHA256", StringComparison.OrdinalIgnoreCase))
                    manifest.MisterSha256 = value;
                else if (String.Equals(key, "RBF_SHA256", StringComparison.OrdinalIgnoreCase))
                    manifest.RbfSha256 = value;
            }

            if (String.IsNullOrWhiteSpace(manifest.TagName) &&
                String.IsNullOrWhiteSpace(manifest.MisterAssetName) &&
                String.IsNullOrWhiteSpace(manifest.RbfAssetName))
                return null;

            return manifest;
        }

        public bool MatchesRelease(GroovyReleaseInfo release)
        {
            if (release == null)
                return false;

            if (!String.IsNullOrWhiteSpace(TagName) &&
                !String.IsNullOrWhiteSpace(release.TagName) &&
                !String.Equals(TagName, release.TagName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!String.IsNullOrWhiteSpace(MisterAssetName) &&
                !String.Equals(MisterAssetName, release.MisterAssetName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!String.IsNullOrWhiteSpace(RbfAssetName) &&
                !String.Equals(RbfAssetName, release.RbfAssetName, StringComparison.OrdinalIgnoreCase))
                return false;

            return !String.IsNullOrWhiteSpace(TagName) ||
                !String.IsNullOrWhiteSpace(MisterAssetName) ||
                !String.IsNullOrWhiteSpace(RbfAssetName);
        }

        public static string BuildReadCommand()
        {
            return "[ -f " + GroovyTargetConfigurator.QuoteRemote(RemotePath) + " ] && cat " +
                GroovyTargetConfigurator.QuoteRemote(RemotePath) + " || true";
        }

        public static string BuildWriteCommand(GroovyTargetManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException("manifest");

            string content = manifest.ToText();
            return "mkdir -p " + GroovyTargetConfigurator.QuoteRemote(RemoteDirectory) +
                "; printf %s " + GroovyTargetConfigurator.QuoteRemote(content) +
                " > " + GroovyTargetConfigurator.QuoteRemote(RemotePath);
        }

        private string ToText()
        {
            var builder = new StringBuilder();
            AppendLine(builder, "TAG", TagName);
            AppendLine(builder, "MISTER", MisterAssetName);
            AppendLine(builder, "RBF", RbfAssetName);
            AppendLine(builder, "MISTER_SHA256", MisterSha256);
            AppendLine(builder, "RBF_SHA256", RbfSha256);
            return builder.ToString();
        }

        private static void AppendLine(StringBuilder builder, string key, string value)
        {
            if (!String.IsNullOrWhiteSpace(value))
                builder.Append(key).Append('=').Append(value.Trim()).Append('\n');
        }

        private static string InferTagName(string misterBinaryPath, string groovyRbfPath)
        {
            string path = !String.IsNullOrWhiteSpace(misterBinaryPath) ? misterBinaryPath : groovyRbfPath;
            if (String.IsNullOrWhiteSpace(path))
                return "";

            string directory = Path.GetDirectoryName(path);
            return String.IsNullOrWhiteSpace(directory) ? "" : Path.GetFileName(directory);
        }

        private static string HashFileIfPresent(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "";

            using (SHA256 sha256 = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha256.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
