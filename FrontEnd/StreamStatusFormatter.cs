using System;

namespace MiSTerCast
{
    static class StreamStatusFormatter
    {
        public static string Format(MiSTerCastInterop.StreamStats stats)
        {
            string audio = stats.fpgaAudio != 0 ? "MiSTer audio on" : "MiSTer audio off";
            string sync = stats.fpgaSynced != 0 ? "MiSTer synced" : "MiSTer sync pending";
            return String.Format(
                "Status: Streaming | PC frame {0} | {1}, {2} | {3} | raster line {4}",
                stats.framesSubmitted,
                sync,
                DescribeFrameLag(stats.framesSubmitted, stats.fpgaFrame),
                audio,
                stats.fpgaVCount);
        }

        private static string DescribeFrameLag(UInt32 framesSubmitted, UInt32 fpgaFrame)
        {
            if (framesSubmitted == fpgaFrame)
                return "caught up";

            if (framesSubmitted > fpgaFrame)
            {
                UInt32 lag = framesSubmitted - fpgaFrame;
                return lag == 1 ? "1 frame behind" : String.Format("{0} frames behind", lag);
            }

            UInt32 ahead = fpgaFrame - framesSubmitted;
            return ahead == 1 ? "1 frame ahead" : String.Format("{0} frames ahead", ahead);
        }
    }
}
