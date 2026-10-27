# Building and changing QuickCrop

## Requirements

- Windows 10/11 with .NET Framework 4.8.
- Windows PowerShell 5.1 and the Framework C# compiler at `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- Git only if you want source control.

No NuGet packages, SDK downloads, Node.js, or Python are required. Windows Forms, System.Drawing, WPF imaging/WIC, and JSON serialization all come from .NET Framework.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

`ExecutionPolicy Bypass` applies only to that PowerShell process. It does not change machine policy. Organizations may enforce policies that prevent running these scripts.

Build output is `dist/QuickCrop.exe`. If the destination is locked by a running app, the update is copied to `dist/QuickCrop-updated.exe`; build output explains which path was used. Tests compile against `dist/checks/QuickCrop.exe`, so a running old app cannot accidentally cause tests to use the old assembly. Tests and generated screenshots live in `dist/checks/`.

`package.ps1` runs the tests before packaging the exact tested executable, MIT license, README, and user guide into `dist/QuickCrop-portable.zip`. No input images, session files, machine credentials, or test binaries go into that archive. The archive includes a SHA-256 checksum file alongside it. The executable is unsigned.

## Source map

| File / type | Responsibility |
| --- | --- |
| `QuickCrop.cs` / `Geometry` | Source-pixel crop math |
| `Imaging`, `PreviewInfo` | Reduced-size decoding, EXIF normalization, pixel-based copies, guarded output encoding |
| `ThumbnailLoader` | Bounded parallel thumbnail batches |
| `SessionStore`, `SessionLease` | Validated JSON, atomic replacement, backup, revision check, per-folder writer lease |
| `ExportJob` | Immutable snapshot of crop, rotation, source identity, and output size |
| `CropView`, `Card` | Preview painting, dragging, zoom, selection, rotation, restore |
| `AppTheme`, `CropWheelRouter` | Native theme adaptation and wheel dispatch without stealing focus |
| `MainWindow` | Folder loading, applied target settings, bulk selection, autosave, export orchestration |
| `Tests.cs` | Geometry, DPI, preview/export matching, EXIF, sessions, rendering, decode benchmark |
| `ReviewTests.cs` | Adversarial regression scenarios |

The source is deliberately still compact. Splitting persistence, imaging, and forms into separate files/projects would be sensible if the app grows; no architectural rewrite is required for this small utility.

## Invariants to preserve

1. Crop coordinates are pixels in the source after EXIF orientation and manual left rotations. Preview and export use the same rectangle.
2. A source file's DPI must never change which source pixels get copied. Always use explicit source and destination pixel rectangles.
3. Thumbnail identity is recorded while the same read handle used to decode it is held. Export validates identity under its own read handle before decoding; neither handle permits concurrent writes or deletion.
4. Native UI controls belong to the UI thread. Keep bounded decode work off that thread, and return to the real Windows Forms message loop before creating controls. The test runner uses that message loop as well.
5. Selection and framing are independent persisted values. Selecting or clearing all must not reset crops or repeatedly recount the entire grid for every checkbox.
6. Pending custom dimensions do not reset crops, change saved target dimensions, or permit processing. Apply width and height together.
7. User data is not silently replaced: JSON saves check the loaded revision; output publication never overwrites a file or directory.
8. Corrupt sessions fail visibly without being rewritten. Check size before reading/parsing large JSON and validate every image entry.

## Tests and their limits

The suite covers 10,000 generated crop geometries, four source DPI values, four manual rotations, crop-only and resized pixel correspondence, all eight EXIF orientations, autosave/reopen, Unicode names, stale source identity, locked writers, two app instances, external session edits, invalid/oversized JSON, atomic save failure, name collisions, bulk selection, pending size fields, focus, and light/dark rendering.

The tests use generated images. They do not cover every real-world codec variation, monitor DPI combination, network filesystem, disk-full condition, or Windows version. They are regression checks, not proof that native image decoders are safe against arbitrary hostile files. Thumbnail memory and control virtualization are still open scaling work.

For a local decode benchmark:

```powershell
.\dist\checks\Tests.exe --benchmark
```

This compares the old full-resolution-copy thumbnail path with reduced-size WIC decoding and bounded workers. It uses generated images and a warm local filesystem, excluding card layout; do not treat the result as a guaranteed end-to-end speedup.

## GitHub publishing

The intended repository is `https://github.com/elkhantar/quickcrop`. Generated `dist/` files, session JSON, and lock files are excluded from Git. Put `QuickCrop-portable.zip` and its checksum in a GitHub release rather than committing binaries into source history.

After the repository exists and Git is authenticated, from this directory:

```powershell
git push -u origin main
```

If the remote already contains commits, fetch and integrate them first; do not force-push over remote work. A Windows CI workflow can be added once hosted. All local checks are runnable without external services.
