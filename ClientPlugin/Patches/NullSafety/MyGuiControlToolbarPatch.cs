using System;
using HarmonyLib;
using Sandbox.Game.Screens.Helpers;

namespace ClientPlugin.Patches.NullSafety;

[HarmonyPatch(typeof(MyGuiControlToolbar), "SetupToolbarStyle")]
[HarmonyPatchCategory("Init")]
static class MyGuiControlToolbarSetupToolbarStylePatch
{
    static Exception Finalizer(Exception __exception)
    {
        if (__exception is NullReferenceException)
            return null;

        return __exception;
    }
}
