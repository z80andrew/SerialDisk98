using AtariST.SerialDisk98.Models;
using AtariST.SerialDisk98.Storage;
using System.Collections.Generic;

namespace AtariST.SerialDisk98.Interfaces
{
    public interface IDisk
    {
        DiskParameters Parameters { get; }
        void WriteSectors(int receiveBufferLength, int startSector, byte[] dataBuffer);

        byte[] ReadSectors(int sector, int numberOfSectors);
    }
}