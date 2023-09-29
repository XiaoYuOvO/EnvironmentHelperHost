using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using MahApps.Metro.Controls;

namespace EnvironmentHelperHost;

public class DeviceTask<TParams, TResult> : Task<TResult> where TParams : IDeviceParameters where TResult : IDeviceResult,new()
{
    // private readonly DeviceFunction<TParams, TResult> _function;
    // private readonly TParams _parameters;
    // public event Action<TResult>? Complete;
    // public event Action<DeviceFunction<TParams,TResult>>? Timeout;
    // private readonly SerialPort _port;

    public DeviceTask(SerialPort port, DeviceFunction<TParams, TResult> function, TParams parameters,Action<TResult>? resultCallback = null,Action<DeviceFunction<TParams,TResult>>? timeoutHandler = null) : base(() =>
    {
        try
        {
            var id = Thread.CurrentThread.ManagedThreadId;
            DebugSendBuffer.Instance.Invoke(()=>DebugSendBuffer.Instance.AddMsg("===INVOKE: " + function + " at " + id));
            var deviceResult = function.Invoke(port,parameters);
            resultCallback?.Invoke(deviceResult);
            DebugSendBuffer.Instance.Invoke(()=>DebugSendBuffer.Instance.AddMsg("===ENDINVOKE: " + function + " at " + id));
            return deviceResult;
        }
        catch (DeviceTimeoutException)
        {
            timeoutHandler?.Invoke(function);
            return new TResult();
        }
    }) {}
}