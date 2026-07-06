namespace MiSTerCast
{
    static class ModelineFile
    {
        public const string FileName = "modelines.dat";

        public static string ResolvePath(string executableDirectory, string currentDirectory)
        {
            return BundledFile.ResolvePath(FileName, executableDirectory, currentDirectory);
        }
    }
}
