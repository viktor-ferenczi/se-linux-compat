using System.Diagnostics;
using System.Reflection;
using ClientPlugin.Patches.SystemAbstraction;
using VRage.Utils;

var variables = new[]
{
    "LD_PRELOAD",
    "LD_LIBRARY_PATH",
    "SYSTEM_LD_LIBRARY_PATH",
    "PATH",
    "SYSTEM_PATH",
    "BROWSER_LAUNCH_TEST",
};
var original = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
try
{
    Environment.SetEnvironmentVariable("LD_LIBRARY_PATH", "/steam/lib");
    Environment.SetEnvironmentVariable("SYSTEM_LD_LIBRARY_PATH", "/host/lib");
    Environment.SetEnvironmentVariable("PATH", "/steam/bin");
    Environment.SetEnvironmentVariable("SYSTEM_PATH", "/host/bin");
    Environment.SetEnvironmentVariable("BROWSER_LAUNCH_TEST", "keep");
    foreach (var preload in new[] { null, "/lib/keep.so:/steam/gameoverlayrenderer.so" })
    {
        // Test fixture only: production never sets the current process environment.
        Environment.SetEnvironmentVariable("LD_PRELOAD", preload);
        var before = Environment.GetEnvironmentVariable("LD_PRELOAD");
        var inherited = new ProcessStartInfo().Environment;
        var uri = new Uri("https://example.invalid/?q='\";$()&x=2");
        var info = MyWindowsSystemOpenUrlPatch.CreateStartInfo(uri);
        info.Environment.TryGetValue("LD_PRELOAD", out var actual);
        Check(actual == string.Empty, "preload cleared");
        Check(
            !info.UseShellExecute
                && info.FileName == "xdg-open"
                && info.Arguments == ""
                && info.ArgumentList.SequenceEqual(new[] { uri.ToString() }),
            "direct xdg-open invocation"
        );
        Check(info.Environment["LD_LIBRARY_PATH"] == "/host/lib", "host library path restored");
        Check(info.Environment["PATH"] == "/host/bin", "host executable path restored");
        Check(
            Environment.GetEnvironmentVariable("LD_PRELOAD") == before,
            "parent environment unchanged"
        );
        Check(
            inherited
                .Where(p => p.Key is not ("LD_PRELOAD" or "LD_LIBRARY_PATH" or "PATH"))
                .All(p => info.Environment[p.Key] == p.Value)
                && info.Environment.Keys.Where(key => key != "LD_PRELOAD")
                    .All(inherited.ContainsKey),
            "all other environment entries preserved"
        );
    }
}
finally
{
    foreach (var variable in variables)
        Environment.SetEnvironmentVariable(variable, original[variable]);
}

CheckPrefixResult("https://example.invalid/", true, "successful launch returns true to the game");
BrowserLaunchTests.BrowserProcess.Executable = "/missing-linux-compat-test/browser";
CheckPrefixResult(
    "https://example.invalid/",
    false,
    "Win32Exception returns false for the game's existing browser-failure popup"
);
Check(
    MyLog.Default.Messages.Any(m => m.Contains("desktop URL launcher")),
    "missing executable logged"
);
foreach (var url in new[] { "not a URI", "file:///etc/passwd", "steam://install/1" })
    CheckPrefixResult(url, false, "invalid or unsupported URL reports failure");
Console.WriteLine("Browser launch checks passed.");

static void CheckPrefixResult(string url, bool expected, string message)
{
    object[] parameters = { url, !expected };
    var runOriginal = (bool)
        typeof(MyWindowsSystemOpenUrlPatch)
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, parameters)!;
    Check(!runOriginal, "prefix skips the original Windows method");
    Check((bool)parameters[1] == expected, message);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
    Console.WriteLine("PASS " + message);
}
