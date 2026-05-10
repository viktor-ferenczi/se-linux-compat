using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Platform.VideoMode;

namespace ClientPlugin.Patches.SystemAbstraction;

// Ports Topic 3.2 (commit 54caa3ac) from dotnet-game-local.
//
// MyVideoSettingsManager.OnVideoAdaptersResponse iterates the reported
// adapters and sets MySandboxGame.ShowIsBetterGCAvailableNotification true
// whenever it sees a non-current adapter whose Priority is higher than the
// current one's. The next MySandboxGame.Update tick consumes that flag and
// pops up a "We have detected a better graphics card available" warning
// dialog.
//
// On Linux the adapter priority comes from DXVK / the proxy DXGI layer and
// does not reflect actual GPU capability ranking — multi-GPU systems
// (laptop iGPU + dGPU, hybrid setups) get the warning every startup even
// when the currently selected adapter is the right choice. The dialog is
// noise; the user has already picked an adapter via config.
//
// Postfix on OnVideoAdaptersResponse forces the flag back to false after
// the stock method has set it. This keeps the rest of the adapter scan
// (m_adapters, m_recommendedAspectRatio, GpuUnderMinimum) intact and only
// suppresses the dialog.
[HarmonyPatch(typeof(MyVideoSettingsManager), "OnVideoAdaptersResponse")]
[HarmonyPatchCategory("Finish")]
static class MyVideoSettingsManagerOnVideoAdaptersResponsePatch
{
    [HarmonyPostfix]
    static void Postfix()
    {
        MySandboxGame.ShowIsBetterGCAvailableNotification = false;
    }
}
