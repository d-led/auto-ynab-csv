using System.Runtime.InteropServices;

namespace AutoYnabCsv.Interactions;

public enum CurrentOs
{
    Windows,
    Mac,
    Unknown
}

public static class WithOs
{
    public static CurrentOs GetOsPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CurrentOs.Windows;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return CurrentOs.Mac;
        }
        
        return CurrentOs.Unknown;
    }
}