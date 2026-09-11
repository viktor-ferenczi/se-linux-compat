using System;
using System.Collections.Generic;
using ClientPlugin.Patches.PathHandling;

namespace VicinityModelPathTests;

/// <summary>
/// Exercises the rewrite the client applies to the model paths a server sends with its
/// vicinity-cache answer. Pure string mapping over a fabricated mod set: nothing is read
/// from disk and no game assembly is involved.
/// </summary>
internal static class Program
{
    // The mods observed on SpiroGames-JunkYard Paradise, as this client holds them.
    private const string Workshop = "/home/user/.steam/steam/steamapps/workshop/content/244850";

    private static readonly Dictionary<string, string> Mods = new()
    {
        ["3768506853"] = Workshop + "/3768506853",
        ["3770888593"] = Workshop + "/3770888593",
        ["3770315903"] = Workshop + "/3770315903",
    };

    private static int s_failures;
    private static int s_checks;

    private static int Main()
    {
        WindowsServerPaths();
        LinuxServerPaths();
        NonMatchingPaths();
        RootedClassification();

        Console.WriteLine();
        Console.WriteLine($"{s_checks - s_failures}/{s_checks} checks passed");
        if (s_failures > 0)
            Console.WriteLine($"FAILED: {s_failures}");
        return s_failures == 0 ? 0 : 1;
    }

    // ---- cases -----------------------------------------------------------------------------

    /// <summary>A Torch instance root on a Windows drive, both separator styles.</summary>
    private static void WindowsServerPaths()
    {
        const string instance = @"G:\Space\Season4\Pertam\Instance\content\244850";

        Remaps(
            "windows backslashes",
            instance + @"\3768506853\Models\Cubes\small\RetroCockpit.mwm",
            Workshop + "/3768506853/Models/Cubes/small/RetroCockpit.mwm"
        );
        Remaps(
            "windows forward slashes",
            instance.Replace('\\', '/') + "/3770888593/Models/SmallScrapBeacon.mwm",
            Workshop + "/3770888593/Models/SmallScrapBeacon.mwm"
        );
    }

    /// <summary>A Linux host, and a layout that does not spell out the Steam app id.</summary>
    private static void LinuxServerPaths()
    {
        Remaps(
            "linux instance",
            "/srv/se/instance/content/244850/3770315903/Models/RetractableSolarPanel.mwm",
            Workshop + "/3770315903/Models/RetractableSolarPanel.mwm"
        );
        Remaps(
            "flat mod folder",
            "/srv/se/mods/3770315903/Models/Panel.mwm",
            Workshop + "/3770315903/Models/Panel.mwm"
        );
        // The id closest to the root is the mod root; a deeper one is part of its content.
        Remaps(
            "outermost id wins",
            "/srv/mods/3768506853/Models/3770888593/Part.mwm",
            Workshop + "/3768506853/Models/3770888593/Part.mwm"
        );
    }

    private static void NonMatchingPaths()
    {
        Keeps("vanilla relative", @"Models\Cubes\small\Cockpit.mwm");
        Keeps("unknown mod id", "/srv/se/instance/content/244850/1234567890/Models/X.mwm");
        // The app id is not a mod id, and a numeric file name is not a folder.
        Keeps("app id only", "/srv/se/instance/content/244850/Models/X.mwm");
        Keeps("id as file name", "/srv/se/instance/content/3768506853");
        Keeps("empty", "");
        Keeps("null", null);
    }

    private static void RootedClassification()
    {
        Rooted("linux absolute", "/srv/se/x.mwm", true);
        Rooted("windows drive", @"G:\Space\x.mwm", true);
        Rooted("unc", @"\\host\share\x.mwm", true);
        Rooted("content relative", @"Models\Cubes\small\Cockpit.mwm", false);
        Rooted("empty", "", false);
        Rooted("null", null, false);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static void Remaps(string label, string sent, string expected)
    {
        s_checks++;
        if (!VicinityModelPathRemap.TryRemapToLocalMod(sent, Mods, out var actual))
        {
            Report(label, expected, "<not remapped>");
            return;
        }
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            Report(label, expected, actual);
            return;
        }
        Console.WriteLine("  ok   " + label);
    }

    private static void Keeps(string label, string sent)
    {
        s_checks++;
        if (VicinityModelPathRemap.TryRemapToLocalMod(sent, Mods, out var actual))
        {
            Report(label, "<not remapped>", actual);
            return;
        }
        Console.WriteLine("  ok   " + label);
    }

    private static void Rooted(string label, string path, bool expected)
    {
        s_checks++;
        var actual = VicinityModelPathRemap.IsRooted(path);
        if (actual != expected)
        {
            Report("rooted: " + label, expected.ToString(), actual.ToString());
            return;
        }
        Console.WriteLine("  ok   rooted: " + label);
    }

    private static void Report(string label, string expected, string actual)
    {
        s_failures++;
        Console.WriteLine("  FAIL " + label);
        Console.WriteLine("       expected: " + expected);
        Console.WriteLine("       actual:   " + actual);
    }
}
