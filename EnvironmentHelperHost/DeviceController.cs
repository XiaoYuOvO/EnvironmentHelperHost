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
                try
                {
                    var temperatureHumidityResult = ReadTemperatureHumidity();
                    SensorDataUpdate?.Invoke(temperatureHumidityResult.Temperature, temperatureHumidityResult.Humidity);
                    Thread.Sleep(_interval);
                }
                catch (ThreadInterruptedException)
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
                    task?.Start();
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
        // _deviceCommunicationThread.Start();
    }

    public void StartReadTemperature()
    {
        _temperatureUpdateThread.Start();
    }

    public bool IsOpen()
    {
        return _serialPort.IsOpen;
    }

    public void RegisterErrorHandler(SerialErrorReceivedEventHandler handler)
    {
        _serialPort.ErrorReceived += handler;
    }

    private TemperatureHumidityResult ReadTemperatureHumidity()
    {
        return DeviceFunctions.ReadTemperatureHumidity.Invoke(_serialPort, VoidParameter.Instance);
    }

    public void SetSystemTime(int timestamp)
    {
        DeviceFunctions.SetSystemTime.Invoke(_serialPort, timestamp);
    }

    public void SetTempLimit(float limit)
    {
        DeviceFunctions.SetTempLimit.Invoke(_serialPort, limit);
    }

    public float GetTempLimit()
    {
        return DeviceFunctions.GetTempLimit.Invoke(_serialPort, VoidParameter.Instance);
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