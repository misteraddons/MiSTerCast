using System;

namespace MiSTerCast
{
    static class ModelineAutofill
    {
        public static bool TryBuild(Modeline template, ushort hactive, ushort vactive, bool interlace, out Modeline modeline, out string error)
        {
            modeline = new Modeline();

            if (hactive == 0 || vactive == 0)
            {
                error = "Active width and height must be greater than 0.";
                return false;
            }

            int hbegin = hactive + (template.hbegin - template.hactive);
            int hend = hbegin + (template.hend - template.hbegin);
            int htotal = hend + (template.htotal - template.hend);
            int vbegin = vactive + (template.vbegin - template.vactive);
            int vend = vbegin + (template.vend - template.vbegin);
            int vtotal = vend + (template.vtotal - template.vend);

            if (hbegin < 0 || hend < 0 || htotal <= 0 || vbegin < 0 || vend < 0 || vtotal <= 0 ||
                hbegin > ushort.MaxValue || hend > ushort.MaxValue || htotal > ushort.MaxValue ||
                vbegin > ushort.MaxValue || vend > ushort.MaxValue || vtotal > ushort.MaxValue)
            {
                error = "Autofilled timings exceed the supported range.";
                return false;
            }

            double refreshHz = GetRefreshHz(template);
            double interlaceFactor = interlace ? 2.0 : 1.0;

            modeline = new Modeline
            {
                name = "Custom",
                pclock = Math.Round(refreshHz * htotal * vtotal / interlaceFactor / 1000000.0, 3),
                hactive = hactive,
                hbegin = (ushort)hbegin,
                hend = (ushort)hend,
                htotal = (ushort)htotal,
                vactive = vactive,
                vbegin = (ushort)vbegin,
                vend = (ushort)vend,
                vtotal = (ushort)vtotal,
                interlace = interlace
            };

            error = "";
            return true;
        }

        public static double GetRefreshHz(Modeline modeline)
        {
            if (modeline.htotal == 0 || modeline.vtotal == 0)
                return 0;

            double interlaceFactor = modeline.interlace ? 2.0 : 1.0;
            return modeline.pclock * 1000000.0 * interlaceFactor / modeline.htotal / modeline.vtotal;
        }
    }
}
