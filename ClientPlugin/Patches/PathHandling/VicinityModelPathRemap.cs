using System.Collections.Generic;

namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// The string surgery behind <c>VicinityModelPaths</c>, kept free of game references so
/// it can be compiled verbatim by <c>tests/vicinity-model-paths</c>.
/// </summary>
public static class VicinityModelPathRemap
{
    /// <summary>
    /// Replaces everything up to and including the path segment naming a loaded mod
    /// with this client's own folder for that mod. How the server arranges its mod
    /// folders is its own business; below that segment the contents are identical on
    /// both sides, because both sides hold the same published item.
    /// </summary>
    /// <param name="path">A model path as the server sent it.</param>
    /// <param name="modFolders">This client's mod folders, keyed by published file id.</param>
    public static bool TryRemapToLocalMod(
        string path,
        IReadOnlyDictionary<string, string> modFolders,
        out string remapped
    )
    {
        remapped = null;
        if (string.IsNullOrEmpty(path) || modFolders == null || modFolders.Count == 0)
            return false;

        var normalized = path.Replace('\\', '/');
        var start = 0;
        while (true)
        {
            var end = normalized.IndexOf('/', start);
            // The trailing segment is the file name, never a mod folder.
            if (end < 0)
                return false;

            if (
                IsPublishedFileId(normalized, start, end)
                && modFolders.TryGetValue(normalized.Substring(start, end - start), out var folder)
                && !string.IsNullOrEmpty(folder)
            )
            {
                remapped = folder.TrimEnd('/') + normalized.Substring(end);
                return true;
            }

            start = end + 1;
        }
    }

    /// <summary>
    /// Rooted as the sender meant it: a Linux absolute path, or a Windows drive or UNC
    /// path. Anything else is a Content-relative vanilla asset, valid as sent.
    /// </summary>
    public static bool IsRooted(string path) =>
        !string.IsNullOrEmpty(path)
        && (path[0] == '/' || path[0] == '\\' || (path.Length >= 2 && path[1] == ':'));

    // Every key is a decimal published file id, so vanilla paths never allocate here.
    private static bool IsPublishedFileId(string path, int start, int end)
    {
        if (end <= start)
            return false;
        for (int i = start; i < end; i++)
        {
            if (path[i] < '0' || path[i] > '9')
                return false;
        }
        return true;
    }
}
