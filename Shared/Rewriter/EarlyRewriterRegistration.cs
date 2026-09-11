#if MAGNETAR
using System;
using System.Collections;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ClientPlugin.Rewriter;

/// <summary>
/// Registers the Windows-semantics pass with DotNetCompat's compiler hook before the server
/// compiles the auto-loaded world's mods. Magnetar instantiates plugins after that compilation,
/// so its normal static <c>Plugin.Rewrite</c> discovery is too late for the first session.
/// </summary>
internal static class EarlyRewriterRegistration
{
    private const string HookTypeName = "ServerPlugin.Rewriter.CompilerHookExtensions";

    private static bool registered;

    public static void Register()
    {
        if (registered)
            return;

        try
        {
            var hookType = FindHookType();
            if (hookType == null)
                throw new InvalidOperationException(
                    $"No loaded assembly exports {HookTypeName}. Loaded assemblies: "
                        + string.Join(
                            ", ",
                            AppDomain
                                .CurrentDomain.GetAssemblies()
                                .Select(assembly => assembly.GetName().Name)
                                .OrderBy(name => name, StringComparer.Ordinal)
                        )
                );

            var field = hookType.GetField("RewriterFactories");
            if (field?.GetValue(null) is not IList factories)
                throw new InvalidOperationException(
                    $"{HookTypeName}.RewriterFactories is unavailable or incompatible"
                );

            Func<SemanticModel, CSharpSyntaxRewriter> factory =
                model => new WindowsSemanticsRewriter(model);
            factories.Add(factory);
            registered = true;
            Log("Windows-semantics rewriter registered with the early compiler hook");
        }
        catch (Exception ex)
        {
            Log($"ERROR: Early Windows-semantics rewriter registration failed: {ex}");
            throw;
        }
    }

    private static Type FindHookType()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            // GitHub and dev-folder builds receive randomized assembly names, so the exported
            // extension type is the only stable identity. Exclude this assembly because plugin
            // projects share the ServerPlugin root namespace.
            if (assembly == typeof(EarlyRewriterRegistration).Assembly)
                continue;

            var type = assembly.GetType(HookTypeName, throwOnError: false);
            if (type != null)
                return type;
        }

        return null;
    }

    private static void Log(string message)
    {
        Console.WriteLine($"[LinuxCompat] {message}");
        try
        {
            VRage.Utils.MyLog.Default?.WriteLineAndConsole($"[LinuxCompat] {message}");
        }
        catch
        {
            // MyLog may not be available during Preloader.Finish.
        }
    }
}
#endif
