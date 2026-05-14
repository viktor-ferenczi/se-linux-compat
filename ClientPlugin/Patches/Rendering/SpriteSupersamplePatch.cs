using System;
using HarmonyLib;
using SharpDX.DXGI;
using VRage.Render11.Common;
using VRage.Render11.Resources;
using VRage.Render11.Sprites;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

// Supersamples HUD/UI sprites by rasterizing them into an oversized offscreen
// RTV and then blitting that down to the backbuffer.
//
// Threading contract (important — earlier revision crashed DXVK because it
// got this wrong):
//
//   * MyRender11.RenderMainSprites           -> render thread
//   * MyRender11.RenderMainSpritesWorker     -> ParallelTasks worker thread
//       └─ MySpritesManager.Render           -> ParallelTasks worker thread
//   * MyRender11.ConsumeMainSprites          -> render thread (post-join)
//
// Anything that touches the D3D11 immediate context (BorrowRtv, ClearRtv,
// CopyToRT, …) MUST run on the render thread. The MySpritesManager.Render
// prefix below runs on a worker thread, so it is restricted to a pure
// argument rewrite (swap rtv + scale viewports). The actual GPU work is
// split between a render-thread prepare prefix on RenderMainSprites and a
// render-thread blit postfix on ConsumeMainSprites.
[HarmonyPatchCategory("Finish")]
static class SpriteSupersampleState
{
    internal const int SupersampleScale = 4;

    // Written on the render thread before the sprite worker is dispatched,
    // read on the worker thread, released on the render thread. The worker
    // dispatch / join performed by RenderMainSprites/ConsumeMainSprites
    // provides the happens-before relationship.
    internal static IBorrowedRtvTexture BackbufferSpritesTarget;

    // Render thread: borrow + clear the supersampled target before the
    // sprite worker runs.
    internal static void PrepareBackbufferSpritesTarget()
    {
        var resolution = MyRender11.ResolutionI;
        if (resolution.X <= 0 || resolution.Y <= 0)
        {
            BackbufferSpritesTarget = null;
            return;
        }

        int width = (int)MathF.Round(resolution.X * SupersampleScale);
        int height = (int)MathF.Round(resolution.Y * SupersampleScale);

        BackbufferSpritesTarget = MyManagers.RwTexturesPool.BorrowRtv(
            "LinuxSpriteSupersample",
            width,
            height,
            Format.B8G8R8A8_UNorm_SRgb);

        MyImmediateRC.RC.ClearRtv(BackbufferSpritesTarget, default);
    }

    // Worker thread: pure argument rewrite, no D3D11 calls.
    internal static bool TryRedirectBackbufferSprites(
        ref IRtvBindable rtv,
        ref MyViewport viewportRtvBound,
        ref MyViewport viewportRtvFull,
        ref Vector2 viewportSizeWrittenIntoShaders)
    {
        if (!object.ReferenceEquals(rtv, MyRender11.Backbuffer))
            return false;

        var target = BackbufferSpritesTarget;
        if (target == null)
            return false;

        rtv = target;
        viewportRtvBound = ScaleViewport(viewportRtvBound, SupersampleScale);
        viewportRtvFull = ScaleViewport(viewportRtvFull, SupersampleScale);
        // Keep sprite layout in normal backbuffer coordinates; only the
        // rasterization target is supersampled.
        return true;
    }

    // Render thread: blit the supersampled result into the backbuffer and
    // release the borrowed RTV.
    internal static void BlitToBackbuffer()
    {
        var target = BackbufferSpritesTarget;
        if (target == null)
            return;

        MyCopyToRT.Run(
            MyRender11.Backbuffer,
            target,
            alphaBlended: true,
            new MyViewport(MyRender11.Backbuffer.Size),
            shouldStretch: true);

        target.Release();
        BackbufferSpritesTarget = null;
    }

    static MyViewport ScaleViewport(MyViewport viewport, float scale)
    {
        return new MyViewport(
            viewport.OffsetX * scale,
            viewport.OffsetY * scale,
            viewport.Width * scale,
            viewport.Height * scale);
    }
}

// Render-thread-side prepare. Runs before the parallel sprite worker is
// dispatched, so all D3D11 immediate-context work stays on the render
// thread. Priority.First so it runs before RenderMainSpritesPatch.Prefix,
// which returns false to replace the original body.
[HarmonyPatch(typeof(MyRender11), "RenderMainSprites", new Type[0])]
[HarmonyPatchCategory("Finish")]
static class SpriteSupersamplePreparePatch
{
    [HarmonyPriority(Priority.First)]
    static void Prefix()
    {
        SpriteSupersampleState.PrepareBackbufferSpritesTarget();
    }
}

// Worker-thread-side argument rewrite only — no D3D11 calls here.
[HarmonyPatch(typeof(MySpritesManager), nameof(MySpritesManager.Render))]
[HarmonyPatchCategory("Finish")]
static class SpriteSupersampleRenderPatch
{
    static void Prefix(
        ref IRtvBindable rtv,
        ref MyViewport viewportRtvBound,
        ref MyViewport viewportRtvFull,
        ref Vector2 viewportSizeWrittenIntoShaders)
    {
        SpriteSupersampleState.TryRedirectBackbufferSprites(
            ref rtv,
            ref viewportRtvBound,
            ref viewportRtvFull,
            ref viewportSizeWrittenIntoShaders);
    }
}

// Render-thread-side blit + release. ConsumeMainSprites runs after the
// sprite worker has been joined.
[HarmonyPatch(typeof(MyRender11), nameof(MyRender11.ConsumeMainSprites))]
[HarmonyPatchCategory("Finish")]
static class SpriteSupersampleConsumePatch
{
    static void Postfix()
    {
        SpriteSupersampleState.BlitToBackbuffer();
    }
}
