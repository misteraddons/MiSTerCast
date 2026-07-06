using System.IO;

namespace MiSTerCast
{
    static class BundledFile
    {
        public static string ResolvePath(string fileName, string executableDirectory, string currentDirectory)
        {
            string executablePath = Path.Combine(executableDirectory ?? "", fileName);
            if (File.Exists(executablePath))
                return executablePath;

            return Path.Combine(currentDirectory ?? "", fileName);
        }
    }
}
