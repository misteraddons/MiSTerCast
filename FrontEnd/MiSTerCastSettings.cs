using System.IO;

namespace MiSTerCast
{
    class MiSTerCastSettings
    {
        public const int CurrentVersion = 2;

        public string Target = "";
        public int ModelinePresetIndex;
        public string PclockText = "";
        public string HactiveText = "";
        public string HbeginText = "";
        public string HendText = "";
        public string HtotalText = "";
        public string VactiveText = "";
        public string VbeginText = "";
        public string VendText = "";
        public string VtotalText = "";
        public bool Interlaced;
        public int CaptureSourceIndex;
        public int AlignmentIndex;
        public int RotationIndex;
        public bool AudioEnabled;
        public bool PreviewEnabled = true;
        public int CropIndex;
        public string CaptureWidthText = "";
        public string CaptureHeightText = "";
        public string CaptureXOffsetText = "";
        public string CaptureYOffsetText = "";

        public void Save(TextWriter writer)
        {
            writer.WriteLine(CurrentVersion);
            writer.WriteLine(Target);
            writer.WriteLine(ModelinePresetIndex);
            writer.WriteLine(PclockText);
            writer.WriteLine(HactiveText);
            writer.WriteLine(HbeginText);
            writer.WriteLine(HendText);
            writer.WriteLine(HtotalText);
            writer.WriteLine(VactiveText);
            writer.WriteLine(VbeginText);
            writer.WriteLine(VendText);
            writer.WriteLine(VtotalText);
            writer.WriteLine(Interlaced ? 1 : 0);
            writer.WriteLine(CaptureSourceIndex);
            writer.WriteLine(AlignmentIndex);
            writer.WriteLine(RotationIndex);
            writer.WriteLine(AudioEnabled ? 1 : 0);
            writer.WriteLine(PreviewEnabled ? 1 : 0);
            writer.WriteLine(CropIndex);
            writer.WriteLine(CaptureWidthText);
            writer.WriteLine(CaptureHeightText);
            writer.WriteLine(CaptureXOffsetText);
            writer.WriteLine(CaptureYOffsetText);
        }

        public static bool TryLoad(TextReader reader, out MiSTerCastSettings settings, out string error)
        {
            settings = new MiSTerCastSettings();
            error = "";

            int version;
            if (!int.TryParse(reader.ReadLine(), out version))
            {
                error = "Invalid settings file format.";
                return false;
            }

            if (version > CurrentVersion)
            {
                error = "Unsupported save file version: " + version;
                return false;
            }

            settings.Target = reader.ReadLine() ?? "";
            settings.ModelinePresetIndex = ReadInt(reader);
            settings.PclockText = reader.ReadLine() ?? "";
            settings.HactiveText = reader.ReadLine() ?? "";
            settings.HbeginText = reader.ReadLine() ?? "";
            settings.HendText = reader.ReadLine() ?? "";
            settings.HtotalText = reader.ReadLine() ?? "";
            settings.VactiveText = reader.ReadLine() ?? "";
            settings.VbeginText = reader.ReadLine() ?? "";
            settings.VendText = reader.ReadLine() ?? "";
            settings.VtotalText = reader.ReadLine() ?? "";
            settings.Interlaced = ReadBool(reader);
            settings.CaptureSourceIndex = ReadInt(reader);

            if (version >= 2)
            {
                settings.AlignmentIndex = ReadInt(reader);
                settings.RotationIndex = ReadInt(reader);
                settings.AudioEnabled = ReadBool(reader);
                settings.PreviewEnabled = ReadBool(reader);
                settings.CropIndex = ReadInt(reader);
            }
            else
            {
                settings.AlignmentIndex = 0;
                settings.RotationIndex = ReadInt(reader);
                settings.AudioEnabled = ReadBool(reader);
                settings.PreviewEnabled = true;
                settings.CropIndex = ReadInt(reader);
            }

            settings.CaptureWidthText = reader.ReadLine() ?? "";
            settings.CaptureHeightText = reader.ReadLine() ?? "";
            settings.CaptureXOffsetText = reader.ReadLine() ?? "";
            settings.CaptureYOffsetText = reader.ReadLine() ?? "";
            return true;
        }

        private static int ReadInt(TextReader reader)
        {
            int value;
            return int.TryParse(reader.ReadLine(), out value) ? value : 0;
        }

        private static bool ReadBool(TextReader reader)
        {
            return reader.ReadLine() == "1";
        }
    }
}
