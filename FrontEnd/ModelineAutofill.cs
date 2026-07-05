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

            if (!ModelineValidator.TryValidate(template, out error))
            {
                error = "Template modeline is invalid: " + error;
                return false;
            }

            int hFrontPorch = template.hbegin - template.hactive;
            int hSyncWidth = template.hend - template.hbegin;
            int hBackPorch = template.htotal - template.hend;
            int vFrontPorch = template.vbegin - template.vactive;
            int vSyncWidth = template.vend - template.vbegin;
            int vBackPorch = template.vtotal - template.vend;

            int hbegin = hactive + hFrontPorch;
            int hend = hbegin + hSyncWidth;
            int htotal = hend + hBackPorch;
            int vbegin = vactive + vFrontPorch;
            int vend = vbegin + vSyncWidth;
            int vtotal = vend + vBackPorch;

            if (htotal > ushort.MaxValue || vtotal > ushort.MaxValue)
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

            if (!ModelineValidator.TryValidate(modeline, out error))
            {
                error = "Autofilled modeline is invalid: " + error;
                return false;
            }

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
