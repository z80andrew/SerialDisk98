using AtariST.SerialDisk98.Interfaces;
using AtariST.SerialDisk98.Models;
using AtariST.SerialDisk98.Utilities;
using System;
using System.IO;
using System.IO.Ports;
using System.Threading;
using static AtariST.SerialDisk98.Common.Constants;

namespace AtariST.SerialDisk98.Comms
{
    public class Serial : ISerial, IDisposable
    {
        private readonly SerialPort _serialPort;

        private readonly ILogger _logger;
        private readonly IDisk _localDisk;

        private bool _receiveCompressedData;
        private int _receivedDataCounter;

        private UInt32 _receivedSectorIndex;
        private UInt32 _receivedSectorCount;
        private byte[] _receiverDataBuffer;
        private int _receiverDataIndex;
        private UInt32 _receivedCRC32;
        private byte? _previousByte;
        private bool _isRLERun;

        [Flags]
        private enum SerialFlags { None = 0, Compression = 1};

        private DateTime _transferStartDateTime;

        private ReceiverState _state = ReceiverState.ReceiveStartMagic;

        private readonly bool _compressionIsEnabled;

        public Serial(SerialPortSettings serialPortSettings, IDisk disk, ILogger log, bool compressionIsEnabled)
        {
            _localDisk = disk;
            _logger = log;
            _compressionIsEnabled = compressionIsEnabled;
            _isRLERun = false;

            try
            {
                _serialPort = InitializeSerialPort(serialPortSettings);
                _serialPort.Open();
                _serialPort.DiscardOutBuffer();
                _serialPort.DiscardInBuffer();
            }

            catch (Exception portException) when (portException is IOException || portException is UnauthorizedAccessException)
            {
                _logger.LogException(portException, $"Error opening serial port {serialPortSettings.PortName}");
                throw;
            }

            _state = ReceiverState.ReceiveStartMagic;
        }

        private SerialPort InitializeSerialPort(SerialPortSettings serialSettings)
        {
            SerialPort serialPort = new SerialPort()
            {
                PortName = serialSettings.PortName,
                Handshake = serialSettings.Handshake,
                BaudRate = serialSettings.BaudRate,
                DataBits = serialSettings.DataBits,
                StopBits = serialSettings.StopBits,
                Parity = serialSettings.Parity
            };


            bool useRts = serialSettings.Handshake == Handshake.RequestToSend || serialSettings.Handshake == Handshake.RequestToSendXOnXOff;

            try
            {
                serialPort.RtsEnable = useRts;
            }

            catch (Exception ex)
            {
                _logger.LogException(ex, "Serial error setting RTS");
            }

            try
            {
                serialPort.DtrEnable = useRts;
            }

            catch (Exception ex)
            {
                _logger.LogException(ex, "Serial error setting DTR");
            }

            _logger.Log($"Serial port {serialPort.PortName} opened successfully", LoggingLevel.Debug);

            return serialPort;
        }

        public void Listen()
        {
            byte[] buffer = new byte[8192];
            Action kickoffRead = null;

            kickoffRead = delegate
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
                    {
                        try
                        {
                            int actualLength = _serialPort.BaseStream.EndRead(ar);
                            byte[] received = new byte[actualLength];
                            Buffer.BlockCopy(buffer, 0, received, 0, actualLength);
                            for (int i = 0; i < actualLength; i++) ProcessReceivedByte(Convert.ToByte(buffer[i]));
                        }

                        catch (InvalidOperationException)
                        {
                            _logger.Log($"Serial port read forcibly stopped on {_serialPort.PortName}", LoggingLevel.Debug);
                        }

                        catch (ThreadAbortException)
                        {
                            _logger.Log($"Stopped listening on {_serialPort.PortName}", LoggingLevel.Debug);
                        }

                        catch (Exception ex)
                        {
                            _logger.LogException(ex, "Error reading from serial port");
                        }

                        kickoffRead();

                    }, null);
                }
            };

            kickoffRead();
        }

        private void ProcessReceivedByte(byte Data)
        {
            try
            {
                switch (_state)
                {
                    case ReceiverState.ReceiveStartMagic:

                        switch (_receivedDataCounter)
                        {
                            case 0:
                                if (Data != 0x18)
                                    _receivedDataCounter = -1;
                                break;

                            case 1:
                                if (Data != 0x03)
                                    _receivedDataCounter = -1;
                                break;

                            case 2:
                                if (Data != 0x20)
                                    _receivedDataCounter = -1;
                                break;

                            case 3:
                                if (Data != 0x06)
                                    _receivedDataCounter = -1;
                                break;

                            case 4:
                                switch (Data)
                                {
                                    case 0:
                                        _logger.Log("Received read command.", LoggingLevel.Debug);
                                        _state = ReceiverState.ReceiveReadSectorIndex;
                                        _receivedSectorIndex = 0;
                                        break;

                                    case 1:
                                        _logger.Log("Received write command.", LoggingLevel.Debug);
                                        _state = ReceiverState.ReceiveWriteSectorIndex;
                                        _receivedSectorIndex = 0;
                                        break;

                                    case 2:
                                        _logger.Log("Received send BIOS parameter block command.", LoggingLevel.Debug);
                                        _state = ReceiverState.SendBiosParameterBlock;
                                        break;
                                }

                                _logger.Log($"Receiver state: {_state}", LoggingLevel.All);

                                _receivedDataCounter = -1;
                                break;
                        }

                        break;

                    case ReceiverState.ReceiveReadSectorIndex:
                        switch (_receivedDataCounter)
                        {
                            case 0:
                                _receivedSectorIndex = (_receivedSectorIndex << 8) + Data;
                                break;

                            case 1:
                                _receivedSectorIndex = (_receivedSectorIndex << 8) + Data;
                                _logger.Log($"Received read sector index command - sector {_receivedSectorIndex}", LoggingLevel.Debug);
                                _state = ReceiverState.ReceiveReadSectorCount;
                                _receivedSectorCount = 0;
                                _receivedDataCounter = -1;
                                break;
                        }

                        break;

                    case ReceiverState.ReceiveReadSectorCount:
                        switch (_receivedDataCounter)
                        {
                            case 0:
                                _receivedSectorCount = (_receivedSectorCount << 8) + Data;
                                break;

                            case 1:
                                _receivedSectorCount = (_receivedSectorCount << 8) + Data;
                                _logger.Log($"Received read sector count command - {_receivedSectorCount} sector(s)", LoggingLevel.Debug);
                                _state = ReceiverState.SendData;
                                _receivedDataCounter = -1;
                                break;
                        }

                        break;

                    case ReceiverState.ReceiveWriteSectorIndex:
                        switch (_receivedDataCounter)
                        {
                            case 0:
                                _receivedSectorIndex = (_receivedSectorIndex << 8) + Data;
                                break;

                            case 1:
                                _receivedSectorIndex = (_receivedSectorIndex << 8) + Data;
                                _logger.Log($"Received write sector index command - sector {_receivedSectorIndex}", LoggingLevel.Debug);
                                _state = ReceiverState.ReceiveWriteSectorCount;
                                _receivedSectorCount = 0;
                                _receivedDataCounter = -1;
                                break;
                        }

                        break;

                    case ReceiverState.ReceiveWriteSectorCount:
                        switch (_receivedDataCounter)
                        {
                            case 0:
                                _receivedSectorCount = (_receivedSectorCount << 8) + Data;
                                break;

                            case 1:
                                _receivedSectorCount = (_receivedSectorCount << 8) + Data;
                                _logger.Log($"Received write sector count command  - {_receivedSectorCount} sector(s)", LoggingLevel.Debug);
                                _state = ReceiverState.ReceiveData;
                                _receivedDataCounter = -1;
                                break;
                        }

                        break;

                    case ReceiverState.ReceiveData:
                        if (_receivedDataCounter == 0) ProcessReceiveDataFlags(Data);
                        else if (_receiverDataIndex != _receivedSectorCount * _localDisk.Parameters.BytesPerSector) ReceiveData(Data);
                        break;

                    case ReceiverState.ReceiveCRC32:
                        ReceiveCRC32(Data);
                        break;
                }

                _receivedDataCounter++;

                switch (_state)
                {
                    case ReceiverState.SendBiosParameterBlock:
                        SendBIOSParameterBlock();
                        break;

                    case ReceiverState.SendData:
                        SendData();
                        break;
                }
            }

            catch (Exception ex)
            {
                _logger.LogException(ex, "Serial port error");
            }
        }

        private void ReceiveCRC32(byte data)
        {
            if (_receivedDataCounter == 0)
            {
                _logger.Log($"Receiving CRC32...", LoggingLevel.Debug);
                _receiverDataIndex = 0;
                _receivedCRC32 = 0;
            }

            _receivedCRC32 = (_receivedCRC32 << 8) + data;

            _receiverDataIndex++;

            Console.Write($"\rReceived [{_receiverDataIndex} / 4] CRC32 bytes");

            if (_receiverDataIndex == 4)
            {
                Console.WriteLine();

                var calculatedCRC32 = CRC32.CalculateCRC32(_receiverDataBuffer);

                if (calculatedCRC32 == _receivedCRC32)
                {
                    _logger.Log($"CRC32 match. Local:{calculatedCRC32} Remote:{_receivedCRC32}", LoggingLevel.Debug);
                    _serialPort.BaseStream.WriteByte(Flags.CRC32Match);
                    _localDisk.WriteSectors(_receiverDataBuffer.Length, (int)_receivedSectorIndex, _receiverDataBuffer);
                    _state = ReceiverState.ReceiveStartMagic;
                }

                else
                {
                    _logger.Log($"CRC32 mismatch. Local:{calculatedCRC32} Remote:{_receivedCRC32}", LoggingLevel.Debug);
                    _serialPort.BaseStream.WriteByte(Flags.CRC32Mismatch);
                    _state = ReceiverState.ReceiveData;
                }

                _receivedDataCounter = -1;

                _logger.Log($"Receiver state: {_state}", LoggingLevel.Debug);
            }
        }

        private void ProcessReceiveDataFlags(byte data)
        {
            _receiveCompressedData = (data & Flags.RLECompressionEnabled) == 1;
        }

        private void ReceiveData(byte Data)
        {
            if (_receivedDataCounter == 1)
            {
                if (_receivedSectorCount == 1)
                    _logger.Log("Writing sector " + _receivedSectorIndex + " (" + _localDisk.Parameters.BytesPerSector + " bytes)... ", LoggingLevel.Debug);
                else
                    _logger.Log("Writing sectors " + _receivedSectorIndex + " - " + (_receivedSectorIndex + _receivedSectorCount - 1) + " (" + (_receivedSectorCount * _localDisk.Parameters.BytesPerSector) + " Bytes)... ", LoggingLevel.Debug);


                _receiverDataBuffer = new byte[_receivedSectorCount * _localDisk.Parameters.BytesPerSector];
                _receiverDataIndex = 0;

                _transferStartDateTime = DateTime.Now;

                _isRLERun = false;
                _previousByte = null;
            }

            if (_receiveCompressedData)
            {
                //decompress RLE data
                if (_isRLERun)
                {
                    while (Data > 1)
                    {
                        _receiverDataBuffer[_receiverDataIndex++] = _previousByte.Value;
                        Data--;
                    }

                    _previousByte = null;
                    _isRLERun = false;
                }

                else if (_previousByte.HasValue && _previousByte.Value == Data)
                {
                    _isRLERun = true;
                }

                else
                {
                    _receiverDataBuffer[_receiverDataIndex++] = Data;
                    _previousByte = Data;
                }
            }

            else
            {
                _receiverDataBuffer[_receiverDataIndex++] = Data;
            }

            string percentReceived = ((Convert.ToDecimal(_receiverDataIndex) / _receiverDataBuffer.Length) * 100).ToString("00.00");
            string formattedBytesReceived = _receiverDataIndex.ToString().PadLeft(_receiverDataBuffer.Length.ToString().Length, '0');
            Console.Write($"\rReceived [{formattedBytesReceived} / {_receiverDataBuffer.Length}] bytes {percentReceived}% ");

            if (_receiverDataIndex == _receivedSectorCount * _localDisk.Parameters.BytesPerSector)
            {
                Console.WriteLine();
                _logger.Log("Transfer done (" + (_receiverDataBuffer.LongLength * 10000000 / (DateTime.Now.Ticks - _transferStartDateTime.Ticks)) + " Bytes/s).", LoggingLevel.Info);

                _receivedDataCounter = -1;
                _state = ReceiverState.ReceiveCRC32;
            }
        }

        private void SendData()
        {
            _logger.Log("Sending data...", LoggingLevel.Info);

            if (_receivedSectorCount == 1)
                _logger.Log("Reading sector " + _receivedSectorIndex, LoggingLevel.Debug);
            else
                _logger.Log("Reading sectors " + _receivedSectorIndex + " - " + (_receivedSectorIndex + _receivedSectorCount - 1), LoggingLevel.Debug);

            byte[] sendDataBuffer = _localDisk.ReadSectors(Convert.ToInt32(_receivedSectorIndex), Convert.ToInt32(_receivedSectorCount));

            UInt32 crc32Checksum = CRC32.CalculateCRC32(sendDataBuffer);

            SerialFlags serialFlags = SerialFlags.None;
            
            if(_compressionIsEnabled) serialFlags |= SerialFlags.Compression;

            _logger.Log($"Sending serial flags: {serialFlags}...", LoggingLevel.Debug);
            _serialPort.BaseStream.WriteByte(Convert.ToByte(serialFlags));

            var numUncompressedBytes = sendDataBuffer.Length;

            string sendingMessage = $"Sending {numUncompressedBytes} bytes";

            if ((serialFlags & SerialFlags.Compression) == SerialFlags.Compression)
            {
                sendDataBuffer = Utilities.LZ4.CompressAsStandardLZ4Block(sendDataBuffer);

                sendingMessage = $"Sending {sendDataBuffer.Length} bytes";

                _transferStartDateTime = DateTime.Now;

                byte[] dataLenBuffer = new byte[4];
                dataLenBuffer[0] = (byte)((sendDataBuffer.Length >> 24) & 0xff);
                dataLenBuffer[1] = (byte)((sendDataBuffer.Length >> 16) & 0xff);
                dataLenBuffer[2] = (byte)((sendDataBuffer.Length >> 8) & 0xff);
                dataLenBuffer[3] = (byte)(sendDataBuffer.Length & 0xff);

                float percentageOfOriginalSize = (100 / (float)numUncompressedBytes) * sendDataBuffer.Length;

                _logger.Log($"Compression: { percentageOfOriginalSize.ToString("00.00")}% of { numUncompressedBytes} bytes", LoggingLevel.Debug);

                _serialPort.BaseStream.Write(dataLenBuffer, 0, dataLenBuffer.Length);
            }

            _logger.Log(sendingMessage, LoggingLevel.Info);

            for (int i = 0; i < sendDataBuffer.Length; i++)
            {
                _serialPort.BaseStream.WriteByte(sendDataBuffer[i]);
                string percentSent = ((Convert.ToDecimal(i + 1) / sendDataBuffer.Length) * 100).ToString("00.00");
                Console.Write($"\rSent [{(i + 1).ToString("D" + sendDataBuffer.Length.ToString().Length)} / {sendDataBuffer.Length} Bytes] {percentSent}% ");
            }
            
            Console.WriteLine();

            byte[] crc32Buffer = new byte[4];
            crc32Buffer[0] = (byte)((crc32Checksum >> 24) & 0xff);
            crc32Buffer[1] = (byte)((crc32Checksum >> 16) & 0xff);
            crc32Buffer[2] = (byte)((crc32Checksum >> 8) & 0xff);
            crc32Buffer[3] = (byte)(crc32Checksum & 0xff);

            _logger.Log("Sending CRC32...", LoggingLevel.Debug);

            _serialPort.BaseStream.Write(crc32Buffer, 0, crc32Buffer.Length);

            _state = ReceiverState.ReceiveStartMagic;

            _logger.Log($"Receiver state: {_state}", LoggingLevel.Debug);
        }

        private void SendBIOSParameterBlock()
        {
            _logger.Log($"Sending BIOS parameter block.", LoggingLevel.Debug);

            _serialPort.BaseStream.Write(_localDisk.Parameters.BIOSParameterBlock, 0, _localDisk.Parameters.BIOSParameterBlock.Length);

            _state = ReceiverState.ReceiveStartMagic;

            _logger.Log($"Receiver state: {_state}", LoggingLevel.Debug);
        }

        public void Dispose()
        {
            if (_serialPort != null)
            {
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
            }
        }
    }
}
