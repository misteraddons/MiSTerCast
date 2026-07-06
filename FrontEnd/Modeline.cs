using System;

namespace MiSTerCast
{
    struct Modeline
    {
        public string name;
        public Double pclock;
        public UInt16 hactive;
        public UInt16 hbegin;
        public UInt16 hend;
        public UInt16 htotal;
        public UInt16 vactive;
        public UInt16 vbegin;
        public UInt16 vend;
        public UInt16 vtotal;
        public bool interlace;
    }
}
