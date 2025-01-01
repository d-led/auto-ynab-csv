using System.Diagnostics;
using System.IO;
using AutoYnabCsv.Interactions;

namespace AutoYnabCsv.DesktopApp.Interactions;


public static class WithSystem
{
    public static void RevealFile(string fileName)
    {
        if (!File.Exists(fileName))
            return;
        
        try
        {
             switch(WithOs.GetOsPlatform())
             {
                case CurrentOs.Windows :
                    RevealInExplorer(fileName);
                    break;
                case CurrentOs.Mac: RevealInFinder(fileName);
                    break;
             };
        }
        catch
        {
            // silent failure
        }
    }

    private static void RevealInFinder(string path)
    {
        Process.Start("open", $"-R \"{path}\"");
    }

    private static void RevealInExplorer(string path)
    {
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }
}