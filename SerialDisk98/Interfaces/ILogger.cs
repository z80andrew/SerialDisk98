﻿using AtariST.SerialDisk98.Common;
using System;

namespace AtariST.SerialDisk98.Interfaces
{
    public interface ILogger
    {
        void CreateLogFile(string folderPath, string fileName);
        void Dispose();
        void Log(string message, Constants.LoggingLevel messageLogLevel);
        void LogException(Exception exception, string message = "");
        void LogToFile(string message);
    }
}