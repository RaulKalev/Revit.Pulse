# Update Check — Implementation Idea

## Overview

Add an update-notification feature that runs a background version check on Revit startup. On shutdown, if a newer version is available, a Material Design WPF dialog appears with a link to the Autodesk App Store listing. Fully compliant with Autodesk App Store distribution terms.

---

## How It Works

1. On Revit startup, the plugin fires a background `Task.Run` that fetches a small JSON manifest (hosted by us — GitHub Gist, Azure Blob, etc.)
2. The manifest version is compared against the plugin's `AssemblyVersion`
3. If the manifest version is newer, the result is cached in memory
4. On Revit shutdown, if a newer version was found, a dialog appears
5. The user clicks **"Get Update"** → their browser opens to the Autodesk App Store listing
6. If the network check fails for any reason, it is silently swallowed — Revit is never affected

---

## Files to Create

| File | Type | Purpose |
|------|------|---------|
| `Core/UpdateCheck/UpdateInfo.cs` | New | DTO deserialized from manifest JSON |
| `Core/UpdateCheck/UpdateCheckService.cs` | New | Background HTTP check + cached result |
| `UI/UpdateNotification/UpdateAvailableWindow.xaml` | New | Material Design update dialog |
| `UI/UpdateNotification/UpdateAvailableWindow.xaml.cs` | New | Code-behind for dialog |
| `App.cs` | Modified | Call check on startup; show dialog on shutdown |

---

## Phase 1 — Core Update Service

### `Core/UpdateCheck/UpdateInfo.cs`

Simple DTO with three properties:
- `string Version`
- `string AppStoreUrl`
- `string? ReleaseNotes`

Deserialized from the manifest JSON using Newtonsoft.Json (already in the project).

### `Core/UpdateCheck/UpdateCheckService.cs`

- Static `UpdateInfo? PendingUpdate` field — non-null means an update is waiting
- `public static void StartBackgroundCheck(string manifestUrl)` fires `Task.Run` that:
  1. GETs the manifest JSON via `HttpClient`
  2. Parses `manifest.Version` into `System.Version`
  3. Compares against `Assembly.GetExecutingAssembly().GetName().Version`
  4. If manifest version is newer, sets `PendingUpdate`
  5. Silently swallows all exceptions — a network failure must never break Revit
- Validates manifest URL is HTTPS before making the request
- Validates `AppStoreUrl` is HTTPS and on the `apps.autodesk.com` domain before storing

---

## Phase 2 — Update Dialog

### `UI/UpdateNotification/UpdateAvailableWindow.xaml`

Material Design WPF dialog matching the existing Pulse UI style:
- "Update Available" header
- "Current version: X.X.X → New version: Y.Y.Y" body
- Optional release notes text block (collapsed when empty)
- **"Get Update"** button — opens the Autodesk App Store page in the browser
- **"Remind Me Later"** button — closes the dialog

### `UI/UpdateNotification/UpdateAvailableWindow.xaml.cs`

- Constructor receives the cached `UpdateInfo`
- "Get Update" click handler:
  1. Re-validates `AppStoreUrl` is HTTPS and on `apps.autodesk.com`
  2. `Process.Start(new ProcessStartInfo(appStoreUrl) { UseShellExecute = true })`
  3. Closes the dialog
- "Remind Me Later": `this.Close()`

---

## Phase 3 — Wire Into App.cs

Two small changes to the existing `App.cs`:

```csharp
public Result OnStartup(UIControlledApplication application)
{
    // ... existing ribbon setup ...

    UpdateCheckService.StartBackgroundCheck("https://YOUR_MANIFEST_URL");
    return Result.Succeeded;
}

public Result OnShutdown(UIControlledApplication application)
{
    var pending = UpdateCheckService.PendingUpdate;
    if (pending != null)
        new UpdateAvailableWindow(pending).ShowDialog();

    return Result.Succeeded;
}
```

---

## Manifest JSON Format

A single JSON file hosted anywhere HTTPS (GitHub Gist, Azure Blob, personal server, etc.):

```json
{
  "version": "1.0.1",
  "appStoreUrl": "https://apps.autodesk.com/RVT/en/Detail/Index?id=YOUR_APP_ID",
  "releaseNotes": "Fixed subcircuit assignment bug."
}
```

To ship an update: bump `version` in this file and update `appStoreUrl` if needed. That's it.

---

## Version Comparison

```csharp
new Version(manifest.Version) > Assembly.GetExecutingAssembly().GetName().Version
```

Uses `System.Version`'s built-in four-part ordering. Works on both `net48` and `net8.0-windows` targets.

---

## Security Notes

- Manifest URL must be HTTPS — rejected otherwise
- `AppStoreUrl` validated to be HTTPS + `apps.autodesk.com` before trusting
- No file downloads, no installer execution — zero attack surface
- All exceptions in the background check are swallowed silently

---

## App Store Compliance

This approach is fully compliant with Autodesk App Store terms:
- Autodesk creates and manages the installer for first-time installs (as normal)
- The plugin only does an informational HTTP GET to check a version number
- The "Get Update" action simply opens a browser to the official App Store page
- No self-updating, no background installer execution, no bypassing Autodesk's distribution

---

## Verification Steps

1. Set `AssemblyVersion` to `0.9.0.0` locally and host a manifest with `"version": "1.0.0"` — confirm the dialog appears at shutdown
2. Click "Get Update" — confirm the browser opens to the correct App Store URL
3. Point manifest at an unreachable URL — confirm Revit starts and shuts down silently with no errors
4. Build both `net48` and `net8.0-windows` targets and confirm 0 errors
