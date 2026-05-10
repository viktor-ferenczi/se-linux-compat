// Skip the Win32-only STA worker thread that the textbox controls spin up
// to read the clipboard. On .NET 9 Linux the very first line of
// Sandbox.Graphics.GUI.MyGuiControlTextbox+MyGuiControlTextboxSelection.PasteText
// does:
//
//     Thread thread = new Thread(PasteFromClipboard);
//     thread.SetApartmentState(ApartmentState.STA);   // <-- throws here
//
// Thread.SetApartmentState(STA) is a Windows-only COM apartment configuration
// and throws System.PlatformNotSupportedException("COM Interop is not
// supported on this platform") on the Linux .NET runtime, blowing up
// Ctrl+V in any text input.
//
// The STA apartment was historically required because the original
// implementation called System.Windows.Forms.Clipboard, which uses OLE and
// must run on an STA thread. Since we redirect MyWindowsSystem.Clipboard to
// SDL3 (see MyWindowsSystemClipboardPatch / SdlClipboard), there is no
// COM-affinity requirement anymore and the worker thread is pure overhead.
//
// We replace PasteText with a synchronous version that reads the clipboard
// on the caller's thread (the main game thread, since HandleInput runs from
// MySandboxGame.Update). MyVRage.Platform.System.Clipboard is the same
// property our SdlClipboard hook overrides, so the SDL3 clipboard text is
// returned to the textbox without any threading at all.

using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage;

namespace ClientPlugin.Patches.SystemAbstraction;

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlTextboxPasteTextPatch
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static MethodBase TargetMethod()
    {
        var selectionType = typeof(MyGuiControlTextbox).GetNestedType("MyGuiControlTextboxSelection", Flags);
        return selectionType?.GetMethod("PasteText", Flags);
    }

    static bool Prefix(object __instance, MyGuiControlTextbox sender)
    {
        // EraseText(sender)
        var selectionType = __instance.GetType();
        selectionType.GetMethod("EraseText", Flags).Invoke(__instance, new object[] { sender });

        StringBuilder textBuilder = AccessTools.FieldRefAccess<MyGuiControlTextbox, StringBuilder>("m_text").Invoke(sender);
        string text = textBuilder.ToString();
        string before = text.Substring(0, sender.CarriagePositionIndex);
        string after = text.Substring(sender.CarriagePositionIndex);

        string clipboardText = ReadClipboardForPaste();
        AccessTools.FieldRefAccess<string>(selectionType, "ClipboardText").Invoke(__instance) = clipboardText;

        string sanitized = clipboardText.Replace("\n", "");
        string toInsert;
        if (sanitized.Length + text.Length <= sender.MaxLength)
        {
            toInsert = sanitized;
        }
        else
        {
            int room = sender.MaxLength - text.Length;
            toInsert = (room <= 0) ? "" : sanitized.Substring(0, room);
        }

        sender.SetText(new StringBuilder(before).Append(toInsert).Append(after));
        sender.CarriagePositionIndex = before.Length + toInsert.Length;

        // Reset(sender)
        selectionType.GetMethod("Reset", Flags).Invoke(__instance, new object[] { sender });
        return false;
    }

    private static string ReadClipboardForPaste()
    {
        string clipboard = MyVRage.Platform.System.Clipboard ?? string.Empty;
        // Match stock PasteFromClipboard: drop entire clipboard if any
        // character is not XML-safe.
        for (int i = 0; i < clipboard.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(clipboard[i]))
                return string.Empty;
        }
        return clipboard;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class MyGuiControlMultilineTextPasteTextPatch
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static MethodBase TargetMethod()
    {
        var selectionType = typeof(MyGuiControlMultilineText).GetNestedType("MyGuiControlMultilineSelection", Flags);
        return selectionType?.GetMethod("PasteText", Flags);
    }

    static bool Prefix(object __instance, MyGuiControlMultilineText sender)
    {
        var selectionType = __instance.GetType();
        selectionType.GetMethod("EraseText", Flags).Invoke(__instance, new object[] { sender });

        StringBuilder textBuilder = AccessTools.FieldRefAccess<MyGuiControlMultilineText, StringBuilder>("m_text").Invoke(sender);
        string text = textBuilder.ToString();
        string before = text.Substring(0, sender.CarriagePositionIndex);
        string after = text.Substring(sender.CarriagePositionIndex);

        string clipboardText = ReadClipboardForPaste();
        AccessTools.FieldRefAccess<string>(selectionType, "ClipboardText").Invoke(__instance) = clipboardText;

        sender.Text = new StringBuilder(before).Append(Regex.Replace(clipboardText, "\r\n", "\n")).Append(after);
        sender.CarriagePositionIndex = before.Length + clipboardText.Length;

        selectionType.GetMethod("Reset", Flags).Invoke(__instance, new object[] { sender });
        return false;
    }

    private static string ReadClipboardForPaste()
    {
        string clipboard = MyVRage.Platform.System.Clipboard ?? string.Empty;
        for (int i = 0; i < clipboard.Length; i++)
        {
            if (!XmlConvert.IsXmlChar(clipboard[i]))
                return string.Empty;
        }
        return clipboard;
    }
}
