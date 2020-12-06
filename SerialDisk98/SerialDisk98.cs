using AtariST.SerialDisk98.Common;
using AtariST.SerialDisk98.Comms;
using AtariST.SerialDisk98.Models;
using AtariST.SerialDisk98.Storage;
using AtariST.SerialDisk98.Utilities;
using SerialDisk98.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using static AtariST.SerialDisk98.Common.Constants;

namespace AtariST.SerialDisk98
{
    public class SerialDisk98
    {
        private static ApplicationSettings _applicationSettings;
        private static Logger _logger;
        private static DiskParameters _diskParameters;
        private static Disk _disk;
        private static Serial _serial;

        private static string FormatEnumParams(Type enumerationType)
        {
            StringBuilder enumString = new StringBuilder();

            foreach (var item in Enum.GetNames(enumerationType))
            {
                enumString.Append(item);
                enumString.Append('|');
            }

            enumString.Remove(enumString.Length - 1, 1);

            return enumString.ToString();
        }

        private static void PrintUsage(ApplicationSettings _applicationSettings)
        {
            Console.WriteLine();

            Console.WriteLine("Usage:");
            Console.WriteLine(System.AppDomain.CurrentDomain.FriendlyName + " [Options] [virtual_disk_path]");
            Console.WriteLine();

            List<String> parameters = new List<string>(Constants.ConsoleParameterMappings.Count);

            foreach(KeyValuePair<string,string> keypair in Constants.ConsoleParameterMappings)
            {
                parameters.Add(keypair.Key);
            }

            Console.WriteLine("Options (default):");
            Console.WriteLine($"{parameters[0]} <disk_size_in_MiB> ({_applicationSettings.DiskSettings.DiskSizeMiB})");
            Console.WriteLine($"{parameters[1]} [{FormatEnumParams(typeof(TOSVersion))}] ({_applicationSettings.DiskSettings.DiskTOSCompatibility})");
            Console.WriteLine($"{parameters[2]} <sectors> ({_applicationSettings.DiskSettings.RootDirectorySectors})");
            Console.WriteLine($"{parameters[3]} [True|False] ({_applicationSettings.IsCompressionEnabled})");

            Console.WriteLine($"{parameters[4]} [port_name] ({_applicationSettings.SerialSettings.PortName})");
            Console.WriteLine($"{parameters[5]} <baud_rate> ({_applicationSettings.SerialSettings.BaudRate})");
            Console.WriteLine($"{parameters[6]} <data_bits> ({_applicationSettings.SerialSettings.DataBits})");
            Console.WriteLine($"{parameters[7]} [{FormatEnumParams(typeof(StopBits))}] ({_applicationSettings.SerialSettings.StopBits})");
            Console.WriteLine($"{parameters[8]} [{FormatEnumParams(typeof(Parity))}] ({_applicationSettings.SerialSettings.Parity})");
            Console.WriteLine($"{parameters[9]} [{FormatEnumParams(typeof(Handshake))}] ({_applicationSettings.SerialSettings.Handshake})");

            Console.WriteLine($"{parameters[10]} [{FormatEnumParams(typeof(Constants.LoggingLevel))}] ({_applicationSettings.LoggingLevel})");
            Console.WriteLine($"{parameters[11]} [log_file_name]");
            Console.WriteLine();

            Console.WriteLine("Serial ports available:");

            foreach (string portName in SerialPort.GetPortNames())
                Console.Write(portName + " ");

            Console.WriteLine();
            Console.WriteLine();
            Console.ReadKey();
        }

        private static string ParseLocalDirectoryPath(string _applicationSettingsPath, string[] args)
        {
            string localDirectoryPath;

            // args length is odd, assume final arg is a path
            if (args.Length % 2 != 0)
            {
                if (Directory.Exists(args[args.Length-1]))
                    localDirectoryPath = args[args.Length-1];

                else
                    throw new Exception($"Could not find path {args[args.Length - 1]}");
            }

            else
            {
                if (Directory.Exists(_applicationSettingsPath))
                    localDirectoryPath = _applicationSettingsPath;

                else
                    throw new Exception($"Could not find path {_applicationSettingsPath}");
            }

            DirectoryInfo localDirectoryInfo = new DirectoryInfo(localDirectoryPath);
            return localDirectoryInfo.FullName;
        }

        private static void ListenForConsoleKeypress()
        {
            ConsoleKeyInfo keyInfo;

            do
            {
                keyInfo = Console.ReadKey(true);
                if ((keyInfo.Modifiers & ConsoleModifiers.Control) != 0 && keyInfo.Key == ConsoleKey.R) _disk.ReimportLocalDirectoryContents();

            } while ((keyInfo.Modifiers & ConsoleModifiers.Control) == 0 || keyInfo.Key != ConsoleKey.X);

            ApplicationExit();
        }

        private static void ApplicationExit()
        {
            _serial.Dispose();
            _logger.Dispose();

            Console.ResetColor();
        }

        public static void Main(string[] args)
        {
            var descriptionAttribute = typeof(SerialDisk98).Assembly
                .GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false)
                .Cast<AssemblyDescriptionAttribute>().First()
                .Description;

            var versionMessage = descriptionAttribute + " v" + typeof(SerialDisk98).Assembly.GetName().Version;

            Console.WriteLine(versionMessage);

            #region Application settings

            ApplicationSettings applicationSettings = new ApplicationSettings();
            
            _applicationSettings = ParameterHelper.MapConfigFiles(applicationSettings);

            if (Array.Find(args, arg => arg == "--help") != null)
            {
                PrintUsage(_applicationSettings);
                return;
            }

            _applicationSettings = ParameterHelper.MapConsoleParameters(applicationSettings, args);

            _applicationSettings.LocalDirectoryPath = ParseLocalDirectoryPath(applicationSettings.LocalDirectoryPath, args);

            if (String.IsNullOrEmpty(_applicationSettings.LocalDirectoryPath)
                || !Directory.Exists(_applicationSettings.LocalDirectoryPath))
            {
                Console.WriteLine($"Local directory path {_applicationSettings.LocalDirectoryPath} not found.");
                return;
            }

            #endregion

            _logger = new Logger(_applicationSettings.LoggingLevel, _applicationSettings.LogFileName);

            _logger.LogToFile(versionMessage);

            _diskParameters = new DiskParameters(_applicationSettings.LocalDirectoryPath, _applicationSettings.DiskSettings, _logger);

            _logger.Log($"Importing local directory contents from {_applicationSettings.LocalDirectoryPath}", Constants.LoggingLevel.Debug);

            _disk = new Disk(_diskParameters, _logger);

            _serial = new Serial(_applicationSettings.SerialSettings, _disk, _logger, _applicationSettings.IsCompressionEnabled);
            Thread serialListener = new Thread(_serial.Listen);
            ThreadManager.AddThread(serialListener);

            _logger.Log($"Baud rate:{_applicationSettings.SerialSettings.BaudRate} | Data bits:{_applicationSettings.SerialSettings.DataBits}" +
                $" | Parity:{_applicationSettings.SerialSettings.Parity} | Stop bits:{_applicationSettings.SerialSettings.StopBits} | Flow control:{_applicationSettings.SerialSettings.Handshake}", LoggingLevel.Info);
            _logger.Log($"Using local directory {_applicationSettings.LocalDirectoryPath} as a {_applicationSettings.DiskSettings.DiskSizeMiB}MiB virtual disk", LoggingLevel.Info);
            _logger.Log($"Compression: " + (_applicationSettings.IsCompressionEnabled ? "Enabled" : "Disabled"), LoggingLevel.Info);
            _logger.Log($"Logging level: { _applicationSettings.LoggingLevel} ", LoggingLevel.Info);

            Console.WriteLine("Press Ctrl-X to quit, Ctrl-R to reimport local disk content.");

            Thread keyboardListener = new Thread(ListenForConsoleKeypress);

            try
            {
                ThreadManager.AddThread(keyboardListener);
            }

            catch (ThreadAbortException ex)
            {
                _logger.Log("Thread cancellation requested", LoggingLevel.Debug);
                _logger.Log(ex.Message, LoggingLevel.Debug);
            }

            Console.ReadKey();
        }
    }
}