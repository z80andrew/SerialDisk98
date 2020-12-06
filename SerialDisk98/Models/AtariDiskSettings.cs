using static AtariST.SerialDisk98.Common.Constants;

namespace AtariST.SerialDisk98.Models
{
    public class AtariDiskSettings
    {
        public TOSVersion DiskTOSCompatibility { get; set; }
        public int RootDirectorySectors { get; set; }
        public int DiskSizeMiB { get; set; }
    }
}