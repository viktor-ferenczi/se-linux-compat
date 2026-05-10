using System.IO;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Ports Topic 4.10 (Crosshair indicator path) from dotnet-game-local.
//
// MyGuiScreenOptionsGame.InitCrosshairIndicators calls
// Directory.EnumerateFiles(Path.Combine(ContentPath, "Textures\\GUI\\Indicators")).
// The backslash-joined path bypasses MyFileSystem and goes straight to
// System.IO which on Linux treats '\\' as part of the filename, so the
// enumeration fails to find the Indicators directory and no crosshair
// textures load.
//
// Replace the method body with an equivalent implementation that uses
// the forward-slash path ("Textures/GUI/Indicators") and preserves the
// HitIndicator filter.
[HarmonyPatch(typeof(MyGuiScreenOptionsGame), "InitCrosshairIndicators")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenOptionsGameInitCrosshairIndicatorsPatch
{
    static bool Prefix(MyGuiScreenOptionsGame __instance)
    {
        var dir = Path.Combine(MyFileSystem.ContentPath, "Textures/GUI/Indicators");
        if (!Directory.Exists(dir))
            return false;

        foreach (var item in Directory.EnumerateFiles(dir))
        {
            if (item.Contains("HitIndicator"))
                __instance.m_crosshairFiles.Add(item);
        }
        return false;
    }
}
