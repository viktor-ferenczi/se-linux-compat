using System;
using HarmonyLib;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

// Mirrors the OperatingSystem.IsLinux() branch in MyRender11.RenderMainSprites()
// (parameterless overload) from the recompiled Linux build.
//
// Stock Windows path:
//   RenderMainSprites(Backbuffer, ScaleMainViewport(vp), vp, ViewportResolutionF)
// where vp is sized to ViewportResolution (the 3D scene RT, which can be
// smaller than the backbuffer due to render-scale / DRS / the
// m_mainViewportScaleFactor centering).
//
// Linux path: render sprites at full backbuffer pixels (ResolutionI) so UI /
// icons / text are not transformed through the 3D scene RT. Preserve the
// SpriteMainViewportScale centering by remapping the stock ViewportResolution
// region into backbuffer space.
[HarmonyPatch(typeof(MyRender11), "RenderMainSprites", new Type[0])]
[HarmonyPatchCategory("Finish")]
static class RenderMainSpritesPatch
{
    static bool Prefix()
    {
        var res = MyRender11.ResolutionI;
        var viewport = new MyViewport(res.X, res.Y);
        var viewportBound = viewport;

        var sceneResolution = MyRender11.ViewportResolution;
        if (sceneResolution.X > 0 && sceneResolution.Y > 0)
        {
            var scaledViewport = MyRender11.ScaleMainViewport(new MyViewport(sceneResolution.X, sceneResolution.Y));
            var scaleX = res.X / (float)sceneResolution.X;
            var scaleY = res.Y / (float)sceneResolution.Y;
            viewportBound = new MyViewport(
                scaledViewport.OffsetX * scaleX,
                scaledViewport.OffsetY * scaleY,
                scaledViewport.Width * scaleX,
                scaledViewport.Height * scaleY);
        }

        var size = new Vector2(res.X, res.Y);
        MyRender11.RenderMainSprites(MyRender11.Backbuffer, viewportBound, viewport, size, null);
        return false;
    }
}
