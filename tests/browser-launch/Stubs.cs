global using Process = BrowserLaunchTests.BrowserProcess;
using System.Collections.Concurrent;

namespace BrowserLaunchTests
{
    // Exercise real process startup and Win32Exception without launching a browser.
    public static class BrowserProcess
    {
        public static string Executable = "/bin/true";

        public static System.Diagnostics.Process Start(System.Diagnostics.ProcessStartInfo info)
        {
            info.FileName = Executable;
            info.UseShellExecute = false; // Do not hand a missing test path to the desktop opener.
            return System.Diagnostics.Process.Start(info);
        }
    }
}

namespace HarmonyLib
{
    public enum MethodType
    {
        Getter,
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch(params object[] _) : Attribute;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatchCategory(string _) : Attribute;
}

namespace VRage.Utils
{
    public sealed class MyLog
    {
        public static MyLog Default { get; } = new();
        public ConcurrentQueue<string> Messages { get; } = new();

        public void WriteLineAndConsole(string message) => Messages.Enqueue(message);
    }
}
