using System;
using System.IO.Ports;
using System.Threading;
using DotNetty.Buffers;
using MahApps.Metro.Controls;


namespace EnvironmentHelperHost
{
    public static class DeviceFunctions
    {
        public static readonly DeviceFunction<VoidParameter, TemperatureHumidityResult> ReadTemperatureHumidity = new(0x2,"ReadTemperatureHumidity");
        public static readonly DeviceFunction<IntegerParameter, VoidResult> SetSystemTime = new(0x3,"SetSystemTime");
        public static readonly DeviceFunction<FloatParameter, VoidResult> SetTempLimit = new(0x4,"SetTempLimit");
        public static readonly DeviceFunction<VoidParameter, FloatResult> GetTempLimit = new(0x5,"GetTempLimit");
    }

    //Parameters
    public interface IDeviceParameters
    {
        void WriteByteData(IByteBuffer byteBuffer);
    }

    public class VoidParameter : IDeviceParameters
    {
        public static readonly VoidParameter Instance = new();
        public void WriteByteData(IByteBuffer byteBuffer)
        {
        }
    }

    public class IntegerParameter : IDeviceParameters
    {
        private readonly int _value;

        private IntegerParameter(int value)
        {
            _value = value;
        }

        public static implicit operator IntegerParameter(int val)
        {
            return new IntegerParameter(val);
        }

        public void WriteByteData(IByteBuffer byteBuffer)
        {
            byteBuffer.WriteInt(_value);
        }
    }
    
    public class FloatParameter : IDeviceParameters
    {
        private readonly float _value;

        private FloatParameter(float val)
        {
            _value = val;
        }

        public static implicit operator FloatParameter(float val)
        {
            return new FloatParameter(val);
        }
        public void WriteByteData(IByteBuffer byteBuffer)
        {
            byteBuffer.WriteInt(BitConverter.SingleToInt32Bits(_value));
        }
    }

    //Results
    public interface IDeviceResult
    {
        void ReadByteData(IByteBuffer bytes);
    }

    public class VoidResult : IDeviceResult
    {
        public void ReadByteData(IByteBuffer bytes)
        {
        }
    }

    public class IntegerResult : IDeviceResult
    {
        private int _value;

        public void ReadByteData(IByteBuffer bytes)
        {
            _value = bytes.ReadInt();
        }

        public static implicit operator int(IntegerResult result)
        {
            return result._value;
        }
    }
    
    public class FloatResult : IDeviceResult
    {
        private static readonly byte[] FloatBuffer = new byte[4];
        private float _value;

        public void ReadByteData(IByteBuffer bytes)
        {
            bytes.ReadBytes(FloatBuffer);
            _value = BitConverter.ToSingle(FloatBuffer);
        }

        public static implicit operator float(FloatResult result)
        {
            return result._value;
        }
    }

    public class TemperatureHumidityResult : IDeviceResult
    {
        private static readonly byte[] FloatBuffer = new byte[4];
        public float Temperature { get; private set; }
        public float Humidity { get; private set; }

        public void ReadByteData(IByteBuffer bytes)
        {
            bytes.ReadBytes(FloatBuffer);
            Humidity = BitConverter.ToSingle(FloatBuffer) * 100;
            bytes.ReadBytes(FloatBuffer);
            Temperature = BitConverter.ToSingle(FloatBuffer);
        }
    }

    public class DeviceFunction<TParma, TResult> where TParma : IDeviceParameters where TResult : IDeviceResult, new()
    {
        private static readonly UnpooledByteBufferAllocator UnpooledByteBufferAllocator = new();
        private const int HeaderSize = 2;
        private const int CommandSize = 1;
        private readonly byte _commandId;
        private readonly string _name;
        private readonly IByteBuffer _sendBuffer = UnpooledByteBufferAllocator.DirectBuffer(4096);
        private readonly IByteBuffer _dataBuffer = UnpooledByteBufferAllocator.DirectBuffer(4093);
        private readonly IByteBuffer _readBuffer = UnpooledByteBufferAllocator.DirectBuffer(4096);

        public DeviceFunction(byte commandId, string name)
        {
            _commandId = commandId;
            _name = name;
        }

        public override string ToString()
        {
            return _name;
        }

        public TResult Invoke(SerialPort port, TParma parameters)
        {
            lock (port)
            {
                if (!port.IsOpen) throw new InvalidOperationException("Port is not open");
                //Post command to device
                // var writeData = new byte[HeaderSize + CommandSize + byteData.Length];
                // var headerSize = (ushort)writeData.Length;
                // var headerBytes = BitConverter.GetBytes(headerSize);
                // Debug.Assert(headerBytes.Length == HeaderSize);
                // Buffer.BlockCopy(headerBytes,0,writeData,0,headerBytes.Length);
                // var commandBytes = BitConverter.GetBytes(_commandId);
                // Debug.Assert(commandBytes.Length == CommandSize);
                // Buffer.BlockCopy(headerBytes,0,writeData,headerBytes.Length,commandBytes.Length);
                // Buffer.BlockCopy(byteData,0,writeData,HeaderSize + CommandSize,byteData.Length);
                // port.Write(writeData,0,writeData.Length);
                parameters.WriteByteData(_dataBuffer);
                var dataLength = _dataBuffer.WriterIndex;
                if (dataLength > 4093)
                {
                    throw new InvalidOperationException($"Packet size is too large: {dataLength}, Expected max 4093 bytes");
                }

                //Packet header
                _sendBuffer.WriteShort((ushort)(HeaderSize + CommandSize + dataLength));
                //Command id
                _sendBuffer.WriteByte(_commandId);
                //Data
                _sendBuffer.WriteBytes(_dataBuffer);
                //Discard old data
                port.ReadExisting();
                //Read result from device
                port.Write(_sendBuffer.Array, 0, _sendBuffer.WriterIndex);
                DebugSendBuffer.Instance.Invoke(()=>DebugSendBuffer.Instance.AddData(_sendBuffer.Array,_sendBuffer.WriterIndex));
                _sendBuffer.Clear();
                _dataBuffer.Clear();
                return ReceiveData(port);    
            }
        }

        public DeviceTask<TParma, TResult> CreateTask(SerialPort port,TParma parameters, Action<TResult>? resultCallback = null, Action? timeoutCallback = null)
        {
            return new DeviceTask<TParma, TResult>(port, this, parameters, resultCallback,_ => timeoutCallback?.Invoke());
        }
        
        
        private TResult ReceiveData(SerialPort port)
        {
            var waitTime = 0;
            while (port.BytesToRead < 3)
            {
                Thread.Sleep(1);
                waitTime++;
                if (waitTime > 600)
                {
                    throw new DeviceTimeoutException();
                }
            } //Wait for the header and command to come

            var header = new byte[3];
            port.Read(header, 0, 3);
            while (header[0] != 0)
            {
                RaedToScreenEnd(port);
                port.Read(header, 0, 3);
            }
            _readBuffer.WriteBytes(header);
            var length = _readBuffer.ReadShort();
            var commandId = _readBuffer.ReadByte();
            while (port.BytesToRead < length - 3)
            {
            } //Wait for data

            //Read data
            var data = new byte[length - 3];
            port.Read(data, 0, length - 3);
            _readBuffer.WriteBytes(data);
            var deviceResult = new TResult();
            DebugSendBuffer.Instance.Invoke(() => DebugSendBuffer.Instance.AddRecvData(_readBuffer.Array, length));
            deviceResult.ReadByteData(_readBuffer);
            _readBuffer.Clear();
            //Discard them
            port.ReadExisting();
            return deviceResult;
        }

        private void RaedToScreenEnd(SerialPort port)
        {
            int ffCount = 0;
            while (ffCount < 3)
            {
                if (port.ReadByte() == 0xff)
                {
                    ffCount++;
                }
            }
        }
    }
}