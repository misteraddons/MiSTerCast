using System;

namespace MiSTerCast
{
    enum CaptureQuickAction
    {
        Native,
        Scale2,
        Scale3,
        Scale4,
        Scale5,
        Full43,
        Full54
    }

    static class CaptureQuickActions
    {
        public static int GetCropModeIndex(CaptureQuickAction action)
        {
            switch (action)
            {
                case CaptureQuickAction.Native:
                    return 1;
                case CaptureQuickAction.Scale2:
                    return 2;
                case CaptureQuickAction.Scale3:
                    return 3;
                case CaptureQuickAction.Scale4:
                    return 4;
                case CaptureQuickAction.Scale5:
                    return 5;
                case CaptureQuickAction.Full43:
                    return 6;
                case CaptureQuickAction.Full54:
                    return 7;
                default:
                    throw new ArgumentOutOfRangeException("action");
            }
        }
    }
}
