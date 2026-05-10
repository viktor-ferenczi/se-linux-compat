using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Sandbox.Game.Gui;
using VRage.FileSystem;

namespace ClientPlugin.Patches.PathHandling;

// Fixes a path-separator bug in MyGuiBlueprintScreen_Reworked.GetBlueprints
// that hides every locally-saved blueprint from the dialog on Linux.
//
// The original method enumerates Blueprints/local subdirectories and probes
// for "<dir>\\bp.sbc" (hardcoded backslash) — a literal filename on Linux,
// where File.Exists returns false and the entry is silently dropped. The
// blueprint name is also derived via text.Split('\\')[^1], which would yield
// the entire path on Linux if execution ever got that far.
//
// Replace the method with a separator-correct equivalent: Path.Combine for
// the bp.sbc probe and Path.GetFileName for the display name. Behaviour on
// the reference Windows build is unchanged (Path.Combine uses '\\' there).
[HarmonyPatch(typeof(MyGuiBlueprintScreen_Reworked), "GetBlueprints")]
[HarmonyPatchCategory("Finish")]
static class MyGuiBlueprintScreenGetBlueprintsPatch
{
    static bool Prefix(
        MyGuiBlueprintScreen_Reworked __instance,
        string directory,
        MyBlueprintTypeEnum type)
    {
        var data = new List<MyBlueprintItemInfo>();
        if (!Directory.Exists(directory))
            return false;

        var directories = Directory.GetDirectories(directory);
        foreach (var dir in directories)
        {
            var path = Path.Combine(dir, "bp.sbc");
            if (!File.Exists(path))
                continue;

            var name = Path.GetFileName(dir);
            var info = new MyBlueprintItemInfo(type)
            {
                TimeCreated = File.GetCreationTimeUtc(path),
                TimeUpdated = File.GetLastWriteTimeUtc(path),
                BlueprintName = name,
                Size = MyFileSystem.GetStorageSize(dir),
            };
            info.SetAdditionalBlueprintInformation(name, name);
            data.Add(info);
        }

        __instance.SortBlueprints(data, MyBlueprintTypeEnum.LOCAL);
        __instance.AddBlueprintButtons(ref data);
        return false;
    }
}
