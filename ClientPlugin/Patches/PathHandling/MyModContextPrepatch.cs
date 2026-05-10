using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ClientPlugin.Patches.PathHandling;

// Topic 4.7 (Mod context Windows-path interface split).
//
// Upstream adds two explicit interface implementations on MyModContext so
// that the public ModPath / ModPathData getters keep returning the native
// (Linux) path for internal callers, while IMyModContext.ModPath /
// IMyModContext.ModPathData — the mod-facing surface — return a
// backslash-separated Windows path:
//
//   public  string ModPath { get; private set; }                 // unchanged
//   string IMyModContext.ModPath => PathUtils.ToWindowsPath(ModPath);
//
//   public  string ModPathData { get; private set; }             // unchanged
//   string IMyModContext.ModPathData => PathUtils.ToWindowsPath(ModPathData);
//
// A Harmony Postfix on the public getters is NOT equivalent: the postfix
// fires for both caller sets and corrupts internal Path.Combine / cache-
// key sites with mixed separators. See
// memory/feedback_explicit_interface_impl.md for the incident that killed
// the previous attempt. The correct fix has to synthesize a real explicit
// interface method, which is a preloader (Mono.Cecil) job.
//
// We inline PathUtils.ToWindowsPath (null ? null : path.Replace('/', '\\'))
// rather than cross-calling ClientPlugin.Patches.PathHandling.PathHelpers
// from VRage.Game.dll: that would require adding a new AssemblyRef plus
// making PathHelpers public, and the transform is a single Replace call.
//
// The public getters are left byte-identical. VerifyCodeHash guards the
// baseline so a future game update that changes the getter body will fail
// fast at preloader time instead of silently breaking the interface split.
public static class MyModContextPrepatch
{
    public static void Prepatch(AssemblyDefinition asmDef)
    {
        if (asmDef.Name.Name != "VRage.Game")
            return;

        var module = asmDef.MainModule;

        var type = module.GetType("VRage.Game.MyModContext");
        if (type == null)
            return;

        var iface = module.GetType("VRage.Game.ModAPI.IMyModContext");
        if (iface == null)
            return;

        InjectExplicitGetter(type, iface, module, "ModPath");
        InjectExplicitGetter(type, iface, module, "ModPathData");
    }

    private static void InjectExplicitGetter(
        TypeDefinition type, TypeDefinition iface, ModuleDefinition module, string propName)
    {
        var publicGetter = type.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        var backingField = type.Fields.FirstOrDefault(f => f.Name == $"<{propName}>k__BackingField");
        var ifaceGetter = iface.Methods.FirstOrDefault(m => m.Name == "get_" + propName);
        if (publicGetter?.Body == null || backingField == null || ifaceGetter == null)
            return;

        // Record the public getter and pin its IL baseline. If the game's
        // getter body ever deviates from the auto-property pattern, we want
        // preloader to throw rather than silently let a bad MethodImpl ride.
        publicGetter.Body.Instructions.RecordOriginalCode(publicGetter);
        publicGetter.Body.Instructions.VerifyCodeHash(publicGetter, "172f09b2");

        var stringRef = module.TypeSystem.String;

        // Mangled name mirrors the C# compiler's output for an explicit
        // interface implementation.
        var mangled = "VRage.Game.ModAPI.IMyModContext.get_" + propName;

        // Don't double-inject if the prepatch runs twice somehow.
        if (type.Methods.Any(m => m.Name == mangled))
            return;

        var attrs = MethodAttributes.Private
                  | MethodAttributes.Final
                  | MethodAttributes.HideBySig
                  | MethodAttributes.NewSlot
                  | MethodAttributes.Virtual
                  | MethodAttributes.SpecialName;

        var newMethod = new MethodDefinition(mangled, attrs, stringRef)
        {
            HasThis = true,
            IsManaged = true,
            ImplAttributes = MethodImplAttributes.IL | MethodImplAttributes.Managed,
        };

        // MethodImpl override: this method satisfies IMyModContext.get_<Prop>().
        newMethod.Overrides.Add(module.ImportReference(ifaceGetter));

        // Body: return backing == null ? null : backing.Replace('/', '\\');
        //
        //   ldarg.0
        //   ldfld   <Prop>k__BackingField
        //   dup
        //   brfalse.s L_null
        //   ldc.i4.s 47   // '/'
        //   ldc.i4.s 92   // '\\'
        //   callvirt String::Replace(char, char)
        //   ret
        // L_null:
        //   ret
        var stringReplace = module.ImportReference(
            typeof(string).GetMethod("Replace", new[] { typeof(char), typeof(char) }));

        var body = new MethodBody(newMethod);
        var il = body.GetILProcessor();

        var retNull = il.Create(OpCodes.Ret);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, backingField));
        il.Append(il.Create(OpCodes.Dup));
        il.Append(il.Create(OpCodes.Brfalse_S, retNull));
        il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)'/'));
        il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)'\\'));
        il.Append(il.Create(OpCodes.Callvirt, stringReplace));
        il.Append(il.Create(OpCodes.Ret));
        il.Append(retNull);

        newMethod.Body = body;
        type.Methods.Add(newMethod);

        // Record the injected IL next to this file, with a custom suffix so
        // it doesn't collide with the public getter's `original`.
        newMethod.Body.Instructions.RecordCustomCode(newMethod, "injected");
    }
}
