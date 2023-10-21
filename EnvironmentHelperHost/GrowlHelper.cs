using HandyControl.Controls;
using HandyControl.Data;

namespace EnvironmentHelperHost;

public class GrowlHelper
{
    public static void Error(string message)
    {
        var growlInfo = new GrowlInfo
        {
            Message = message,
            IsCustom = true,
            WaitTime = 4
        };
        Growl.Error(growlInfo);
    }
}