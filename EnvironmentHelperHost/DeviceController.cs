using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using HandyControl.Controls;

namespace EnvironmentHelperHost;

public class DeviceController
{
    public delegate void SensorDataUpdateHandler(float temperature, float humidity);

    public event SensorDataUpdateHandler? SensorDataUpdate;
    public event Action? DeviceTimeout;

    private readonly SerialPort _serialPort;
    private readonly Thread _temperatureUpdateThread;
    private readonly Thread _deviceCommunicationThread;
    private readonly Queue<Task> _taskQueue = new();
    private int _interval = 1000;

    public DeviceController(string name)
    {
        _serialPort = new SerialPort();
        _serialPort.PortName = name;
        _serialPort.BaudRate = 115200;
        _serialPort.Parity = Parity.None;
        _serialPort.StopBits = StopBits.One;
        _serialPort.DataBits = 8;
        _temperatureUpdateThread = new Thread(() =>
        {
            while (IsOpen())
            {
                ReadTemperatureHumidity(  result => SensorDataUpdate?.Invoke(result.Temperature, result.Humidity),
                    () => GrowlHelper.Error("无法获取温度湿度! 设备超时"));
                try
                {
                    Thread.Sleep(_interval);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        })
        {
            Name = "TemperatureUpdateThread"
        };
        _deviceCommunicationThread = new Thread(() =>
        {
            while (IsOpen())
            {
                while (_taskQueue.TryDequeue(out var task))
                {
                    task?.RunSynchronously();
                }
                try
                {
                    Thread.Sleep(1);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        })
        {
            Name = "DeviceCommunicationThread"
        };
    }

    public void Open()
    {
        _serialPort.Open();
        _temperatureUpdateThread.Start();
        _deviceCommunicationThread.Start();
    }

    public bool IsOpen()
    {
        return _serialPort.IsOpen;
    }

    public void RegisterErrorHandler(SerialErrorReceivedEventHandler handler)
    {
        _serialPort.ErrorReceived += handler;
    }

    private DeviceTask<VoidParameter, TemperatureHumidityResult> ReadTemperatureHumidity(
        Action<TemperatureHumidityResult> handler, Action? timeoutHandler = null)
    {
        return InvokeFunction(DeviceFunctions.ReadTemperatureHumidity, VoidParameter.Instance,handler,
            timeoutHandler: timeoutHandler);
    }

    public DeviceTask<IntegerParameter, VoidResult> SetSystemTime(int timestamp, Action? timeoutHandler = null)
    {
        return InvokeFunction(DeviceFunctions.SetSystemTime, timestamp, timeoutHandler: timeoutHandler);
    }

    public DeviceTask<FloatParameter, VoidResult> SetTempLimit(float limit, Action? resultHandler,
        Action? timeoutHandler = null)
    {
        return InvokeFunction(DeviceFunctions.SetTempLimit, limit, _ => resultHandler?.Invoke(),
            timeoutHandler: timeoutHandler);
    }

    public DeviceTask<VoidParameter, FloatResult> GetTempLimit(Action<FloatResult> resultCallback,
        Action? timeoutHandler = null)
    {
        return InvokeFunction(DeviceFunctions.GetTempLimit, VoidParameter.Instance, resultCallback,
            timeoutHandler: timeoutHandler);
    }

    private DeviceTask<TParams, TResult> InvokeFunction<TParams, TResult>(DeviceFunction<TParams, TResult> function,
        TParams parameters, Action<TResult>? resultHandler = null, Action? timeoutHandler = null)
        where TParams : IDeviceParameters where TResult : IDeviceResult, new()
    {
        lock (_taskQueue)
        {
            if (!IsOpen())
            {
                throw new InvalidOperationException("The device is not opened!");
            }

            var deviceTask = function.CreateTask(_serialPort, parameters, resultHandler, () =>
            {
                timeoutHandler?.Invoke();
                DeviceTimeout?.Invoke();
            });
            _taskQueue.Enqueue(deviceTask);
            return deviceTask;
        }
    }

    public void SetQueryInterval(int interval)
    {
        _interval = interval;
        _temperatureUpdateThread.Interrupt();
    }

    public void Close()
    {
        _serialPort.Close();
        _temperatureUpdateThread.Interrupt();
    }
}