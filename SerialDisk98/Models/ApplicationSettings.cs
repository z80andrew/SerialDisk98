using System;
using System.IO;
using static AtariST.SerialDisk98.Common.Constants;

namespace AtariST.SerialDisk98.Models
{
    public class ApplicationSettings
    {
        private string _logfileName;

        public SerialPortSettings SerialSettings { get; set; }

        public AtariDiskSettings DiskSettings { get; set; }

        public LoggingLevel LoggingLevel { get; set; }

        public string LocalDirectoryPath{ get; set; }

        public bool IsCompressionEnabled { get; set; }

        public string LogFileName
        {
            get => _logfileName;
            set => _logfileName = String.Join("_", value.Split(Path.GetInvalidFileNameChars()));
        }

        public ApplicationSettings()
        {
            SerialSettings = new SerialPortSettings();
            DiskSettings = new AtariDiskSettings();
        }
    }
}
