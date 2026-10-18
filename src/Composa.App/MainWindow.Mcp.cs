using Avalonia.Input;
using Composa.App.Dialogs;
using Composa.Editing;
using Composa.Filters;
using Composa.IO;

namespace Composa.App;

// Dialog-free entry points shared with the MCP adapter. The normal UI commands keep using the same state.
public sealed partial class MainWindow
{
    internal CameraRawSettings LastCameraRaw => lastCameraRaw;
    internal FilterSettings? LastFilter => lastAppliedFilter;
    internal IReadOnlyList<Shortcut> McpCommands => AllShortcuts;
    internal bool CanvasOnly => canvasOnly;

    internal void RememberMcpFilter(FilterSettings filter)
    {
        if (filter.IsIdentity) return;
        lastAppliedFilter = filter;
        if (filter.Kind == FilterKind.CameraRaw) lastCameraRaw = filter.CameraRaw;
    }

    internal void ActivateMcpDocument(EditorSession target) => SetSession(target);

    internal void ReorderMcpDocument(EditorSession target, int index)
    {
        sessions.Remove(target); sessions.Insert(index, target); RebuildTabs();
    }

    internal void RememberMcpView() { RememberToolSettings(); RebuildOptions(); canvas.InvalidateVisual(); }

    internal void RememberMcpProfile(byte[]? profile,string name)
    {
        if (profile is not { Length: >= 128 } || profile.Length > 16*1024*1024 || !profile.AsSpan(16,4).SequenceEqual("CMYK"u8) || !profile.AsSpan(36,4).SequenceEqual("acsp"u8)) return;
        settings.CmykProfile = profile.ToArray(); settings.CmykProfileName = name; settings.Save();
        colorProof = null; canvas.SetColorProof(null);
    }

    internal Task McpClipboard(string action) => action switch {
        "copy" => Copy(false), "copy_merged" => Copy(true), "cut" => Cut(),
        "paste_in_place" => Paste(PasteKind.InPlace), "paste_into" => Paste(PasteKind.Into), _ => Paste()
    };

    internal void SetMcpShortcut(string id, KeyGesture? gesture, bool reset)
    {
        var command = AllShortcuts.Single(s => s.Id == id);
        if (reset) settings.Shortcuts.Remove(id);
        else settings.Shortcuts[id] = gesture?.ToString() ?? "";
        foreach (var shortcut in AllShortcuts) { shortcut.Gesture = shortcut.Default; if (shortcut.Item != null) shortcut.Item.InputGesture = shortcut.Gesture; }
        ApplyShortcutOverrides(); settings.Save();
    }

    internal async Task<CmykProof> McpColorProof(byte[]? profile = null, string? name = null)
    {
        if (profile == null && colorProof != null) return colorProof;
        var bytes = profile ?? settings.CmykProfile ?? global::ImageMagick.ColorProfiles.CoatedFOGRA39.ToByteArray();
        var title = name ?? settings.CmykProfileName;
        var proof = await Task.Run(() => new CmykProof(title, global::ImageMagick.ColorProfiles.SRGB.ToByteArray(), bytes));
        if (profile != null) { settings.CmykProfile = profile; settings.CmykProfileName = title; settings.Save(); }
        colorProof = proof; canvas.SetColorProof(proof); return proof;
    }
}
