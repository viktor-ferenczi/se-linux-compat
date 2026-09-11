using System.Diagnostics;
using System.Reflection;
using ClientPlugin.Patches.SystemAbstraction;
using VRage.Utils;

var original = Environment.GetEnvironmentVariable("LD_PRELOAD");
try
{
    foreach (
        var (preload, expected) in new (string, string)[]
        {
            (null, null),
            ("", ""),
            ("/lib/keep.so", "/lib/keep.so"),
            (":/steam/32/gameoverlayrenderer.so:/steam/64/gameoverlayrenderer.so", ""),
            (
                "/lib/keep.so /steam/gameoverlayrenderer.so:/lib/also-keep.so",
                "/lib/keep.so:/lib/also-keep.so"
            ),
            (
                "gameoverlayrenderer.so:/lib/gameoverlayrenderer.so.backup",
                "/lib/gameoverlayrenderer.so.backup"
            ),
        }
    )
    {
        // Test fixture only: production never sets the current process environment.
        Environment.SetEnvironmentVariable("LD_PRELOAD", preload);
        var before = Environment.GetEnvironmentVariable("LD_PRELOAD");
        var inherited = new ProcessStartInfo().Environment;
        var info = MyWindowsSystemOpenUrlPatch.CreateStartInfo(
            new Uri("https://example.invalid/?q='\";$()&x=2")
        );
        info.Environment.TryGetValue("LD_PRELOAD", out var actual);
        Check(actual == expected || (expected == "" && actual == null), "selective preload filter");
        Check(
            info.UseShellExecute
                && info.FileName.StartsWith("https://example.invalid/")
                && info.Arguments == ""
                && info.ArgumentList.Count == 0,
            "original shell-execute URL path retained"
        );
        Check(
            Environment.GetEnvironmentVariable("LD_PRELOAD") == before,
            "parent environment unchanged"
        );
        Check(
            inherited.Where(p => p.Key != "LD_PRELOAD").All(p => info.Environment[p.Key] == p.Value)
                && info.Environment.Keys.All(inherited.ContainsKey),
            "all other environment entries preserved"
        );
    }
}
finally
{
    Environment.SetEnvironmentVariable("LD_PRELOAD", original);
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
