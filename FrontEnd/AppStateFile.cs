using System.IO;

namespace MiSTerCast
{
    static class AppStateFile
    {
        public const string LastSaveFileName = "lastsave.dat";

        public static string ResolveLastSavePath(string executableDirectory)
        {
            return Path.Combine(executableDirectory ?? "", LastSaveFileName);
        }
    }
}
