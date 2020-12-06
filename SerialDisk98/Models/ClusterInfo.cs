using System.IO;

namespace AtariST.SerialDisk98.Models
{
    public class ClusterInfo
    {
        public LocalDirectoryContentInfo LocalDirectoryContent;

        public long FileOffset;
        public byte[] DataBuffer;
    }
}
