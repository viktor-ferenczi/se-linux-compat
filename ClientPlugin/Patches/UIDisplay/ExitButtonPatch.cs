using HarmonyLib;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;

namespace ClientPlugin.Patches.UIDisplay;

// Ports Topic 11.2 from dotnet-game-local.
//
// Stock SpaceEngineers.Game's MyGuiScreenMainMenu.CreateMainMenu builds the
// bottom-of-menu exit button as
//
//   m_exitGameButton = MakeButton("Exit", ..., MyCommonTexts.ScreenMenuButtonExitToWindows, ...);
//
// On the native Linux build we want it to read "Exit to Linux".
//
// Earlier attempts:
//   1. Prefix on MyGuiScreenMainMenuBase.MakeButton (ref MyStringId text):
//      no visible effect — MakeButton is small enough that the JIT inlines
//      it into CreateMainMenu, leaving Harmony's method-level detour with
//      no live call site.
//   2. Postfix on MakeButton overriding __result.Text: same reason.
//   3. Transpiler on CreateMainMenu replacing the ldsfld of
//      ScreenMenuButtonExitToWindows with ldstr "Exit to Linux"+
//      MyStringId.GetOrCompute: still showed "Exit to Windows". The cause
//      is Pulsar.Legacy.Patch.Patch_CreateMainMenu — Pulsar registers its
//      own postfix on CreateMainMenu under category "Late" (so it runs
//      after our "Finish"-category patches) which unconditionally rewrites
//      ___m_exitGameButton.Text using Tools.IsNative(). IsNative returns
//      true when STEAM_COMPAT_PROTON is unset, which is the case on the
//      native Linux build, so Pulsar's postfix flips the label back to
//      "Exit to Windows" — the opposite of what we want here.
//
// Working approach (matches Pulsar's own technique, verified by reading
// /home/viktor/dev/se1/Pulsar/Legacy/Patch/Patch_CreateMenu.cs): postfix
// on CreateMainMenu pulling in the private m_exitGameButton field via
// the standard Harmony triple-underscore convention, and overwrite Text
// directly. Use Priority.Last so we sort to the end of the merged
// postfix chain — Harmony orders postfixes by descending priority, so a
// Last-priority entry runs after Pulsar's default-priority postfix.
[HarmonyPatch(typeof(MyGuiScreenMainMenu), "CreateMainMenu")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenMainMenuCreateMainMenuExitTextPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix(MyGuiControlButton ___m_exitGameButton)
    {
        if (___m_exitGameButton != null)
            ___m_exitGameButton.Text = "Exit to Linux";
    }
}
