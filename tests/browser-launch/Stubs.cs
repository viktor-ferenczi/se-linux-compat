using System.Collections.Concurrent;

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
