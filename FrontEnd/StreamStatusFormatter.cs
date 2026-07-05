using System;

namespace MiSTerCast
{
    static class StreamStatusFormatter
    {
        public static string Format(MiSTerCastInterop.StreamStats stats, bool audioRequested)
        {
            string audio = DescribeAudioState(audioRequested, stats.fpgaAudio != 0);
            string sync = stats.fpgaSynced != 0 ? "MiSTer synced" : "MiSTer sync pending";
            return String.Format(
                "Status: Streaming | PC frame {0} | {1}, {2} | dropped {3} | {4}",
                stats.framesSubmitted,
                sync,
                DescribeFrameLag(stats.framesSubmitted, stats.fpgaFrame),
                stats.droppedFrames,
                audio);
        }

        private static string DescribeAudioState(bool audioRequested, bool fpgaAudio)
        {
            string pc = audioRequested ? "PC audio requested" : "PC audio disabled";
            string mister = fpgaAudio ? "MiSTer on" : "MiSTer off";
            return String.Format("{0}, {1}", pc, mister);
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
