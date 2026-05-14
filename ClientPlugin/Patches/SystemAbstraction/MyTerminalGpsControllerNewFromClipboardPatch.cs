// Skip the Win32-only STA worker thread that the GPS terminal tab spins up
// to read the clipboard. Stock
// Sandbox.Game.Screens.Terminal.MyTerminalGpsController.OnButtonPressedNewFromClipboard
// does:
//
//     Thread thread = new Thread(PasteFromClipboard);
//     thread.SetApartmentState(ApartmentState.STA);   // <-- throws here
//     thread.Start();
//     thread.Join();
//
// Thread.SetApartmentState(STA) is a Windows-only COM apartment configuration
// and throws System.PlatformNotSupportedException("COM Interop is not
// supported on this platform") on the Linux .NET runtime, crashing the game
// the moment the user clicks "New from clipboard" in the GPS terminal tab.
//
// Same root cause as MyGuiControlClipboardPastePatches.cs: the STA apartment
// was historically required because the original implementation called
// System.Windows.Forms.Clipboard, which uses OLE and must run on an STA
// thread. Since we redirect MyWindowsSystem.Clipboard to SDL3 (see
// MyWindowsSystemClipboardPatch / SdlClipboard), there is no COM-affinity
// requirement anymore and the worker thread is pure overhead.
//
// We replace OnButtonPressedNewFromClipboard with a synchronous version that
// reads MyVRage.Platform.System.Clipboard on the caller's thread (the main
// game thread). The SDL3 clipboard read returns the host clipboard text and
// is then handed to MyGpsCollection.ScanText, identical to the stock
// behaviour minus the STA dance.

using HarmonyLib;
using Sandbox.Game.Localization;
using Sandbox.Game.Screens.Terminal;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage;
using VRage.Utils;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch(typeof(MyTerminalGpsController), nameof(MyTerminalGpsController.OnButtonPressedNewFromClipboard))]
[HarmonyPatchCategory("Finish")]
static class MyTerminalGpsControllerNewFromClipboardPatch
{
    static bool Prefix(MyTerminalGpsController __instance, MyGuiControlButton senderButton)
    {
        __instance.m_clipboardText = MyVRage.Platform.System.Clipboard;
        if (!string.IsNullOrEmpty(__instance.m_clipboardText))
        {
            MySession.Static.Gpss.ScanText(
                __instance.m_clipboardText,
                MyTexts.Get(MySpaceTexts.TerminalTab_GPS_NewFromClipboard_Desc));
        }
        __instance.m_searchBox.SearchText = string.Empty;
        return false;
    }
}
