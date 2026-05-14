using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace ClientPlugin.Patches.UIDisplay;

// Ports Topic 11.1 from dotnet-game-local, adjusted for the runtime
// reality: the " on .NET <framework>" suffix in MyGuiScreenMainMenuBase.
// DrawAppVersion is NOT in the stock IL — it is injected by the
// se-dotnet-compat plugin (loaded immediately before us). That plugin's
// DrawAppVersion transpiler appends a call to
//
//   ClientPlugin.Patches.Miscellaneous.MyGuiScreenMainMenuBasePatch.AppendFrameworkDescription(string text)
//
// which returns $"{text} on {RuntimeInformation.FrameworkDescription}". So
// the literal " on " never appears as an ldstr inside DrawAppVersion's
// post-stack IL — earlier transpilers on DrawAppVersion (looking for
// `ldstr " on "` or even an ldstr containing " on ") couldn't possibly
// match. Hooking the helper method itself is the right place.
//
// Postfix appends " on Linux" to the helper's result, producing
// "<version> b<build> on .NET <framework> on Linux". User preference
// is to suffix rather than insert mid-string.
//
// Reflection-based TargetMethod / Prepare: the type lives in DotNetCompat.dll
// which we don't compile-link against. If the user runs without
// se-dotnet-compat for some reason, Prepare returns false and the patch
// is silently skipped.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class DotNetCompatAppendFrameworkDescriptionPatch
{
    private const string TypeName = "ClientPlugin.Patches.Miscellaneous.MyGuiScreenMainMenuBasePatch";
    private const string MethodName = "AppendFrameworkDescription";

    static MethodBase TargetMethod()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a =>
            {
                try { return a.GetType(TypeName, throwOnError: false); }
                catch { return null; }
            })
            .FirstOrDefault(t => t != null);

        return type?.GetMethod(MethodName,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);
    }

    static bool Prepare() => TargetMethod() != null;

    static void Postfix(ref string __result)
    {
        if (string.IsNullOrEmpty(__result))
            return;
        __result = __result + " on Linux";
    }
}
