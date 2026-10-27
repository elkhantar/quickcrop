# QuickCrop

A portable Windows desktop tool for manually framing and batch cropping images. No installer, account, network connection, or third-party packages.

This is a small, vibe-coded utility, built with AI assistance over a couple of sessions to solve a specific annoyance: manually choosing a good crop for a folder full of images. It does that job, and it has regression tests and a documented code review. It is still a young, pragmatic tool, with a simple UI and rough edges—not a mature photo editor. Feedback and fixes are welcome.

Documentation: [User guide](docs/USER_GUIDE.md) · [Build and development](docs/DEVELOPMENT.md) · [Adversarial review](docs/REVIEW.md) · [Changelog](CHANGELOG.md).

## Run

For a packaged copy, extract `QuickCrop-portable.zip` and run `QuickCrop.exe`. When building from source, run `dist/QuickCrop.exe`. You can copy just that executable to another Windows 10/11 computer or a USB drive. Requires .NET Framework 4.8 (included in recent Windows 10 and Windows 11). Selections and framing are saved alongside your images; no registry entries are written.

## Workflow

1. **Open folder** loads JPG/JPEG, PNG, BMP, GIF and TIFF files in that folder (not subfolders). Camera EXIF orientation is applied automatically.
2. Pick a resolution preset, or enter both custom pixel dimensions and click **Apply size**. This determines the crop aspect ratio. Changing the ratio resets framing on all cards, keeping selection and rotation. Switching resolutions with the same ratio preserves framing. Unapplied custom edits leave crops alone and disable processing. You can start editing loaded images while the rest of the folder loads.
3. Drag the bright rectangle to place the crop. Wheel up over an image makes the crop smaller (zoom in); wheel down enlarges it to its initial maximal size. Zoom ranges from 1x to 20x. The image itself stays fitted in its card so the surrounding composition remains visible.
4. Editing, rotating left, or resetting a crop selects that image. You can uncheck it afterwards, or use **Select all** / **Select none** without changing framing. The **Fit** button resets crop position and zoom; it does not change the target ratio. Red-dominant images use yellow outlines.
5. Adjust **Card size** to fit your display. Enable **Resize output** for exact requested dimensions (including upscaling). Leave it off to retain the crop's source pixel size, rounded to whole pixels.
6. **Process selected** asks for an output folder. PNG inputs produce PNG; other formats produce quality-95 JPEG. GIF/TIFF use the first frame/page. Alpha is preserved in PNG; JPEG flattens transparency onto white.

Outputs use `name_crop.ext`, then `name_crop_1.ext`, etc. Existing files and originals are never overwritten, even when exporting into the input folder. A failed image does not stop the batch; a completion dialog reports errors.

## Remembering selections and crops

QuickCrop automatically saves a small `.quickcrop.json` file **inside the input folder**, shortly after edits. It remembers each image's selection checkbox, crop rectangle, and left rotation, plus the folder's target dimensions, resize option, and card size. Reopen the same folder to resume. Copying/moving the entire folder, including this file, carries the session with it. Filenames are relative to that folder.

The footer reports whether edits are saved or saving failed. Autosave waits until folder loading finishes successfully. Closing during image loading cancels it and discards that load's edits without creating or changing the session JSON or backup. After loading, pending edits are flushed before switching folders, processing, or closing. Writes use an atomic replacement, with the previous save retained as `.quickcrop.json.bak`. If the session is malformed or has an unsupported version, it is kept unchanged and autosave is disabled for that folder, with a visible explanation. A folder must be writable to save edits.

An empty `.quickcrop.lock` coordinates instances: only one instance can save a folder at a time. Additional instances have read-only sessions, and external JSON edits detected since loading are not overwritten. The lock file can remain after closing; its existence does not mean it is locked. Custom target dimensions are saved only after **Apply size**. See the user guide for recovery and conflict handling.

An image whose file size, last-modified timestamp, or dimensions changed starts with a fresh crop and unselected checkbox; stale crops are not applied to changed sources. Export also rejects a source changed since loading. Renamed files start fresh. The session holds settings only, not image copies or thumbnails. You can delete `.quickcrop.json` to reset a folder's session while the app is closed.

## Preview and export match

The crop rectangle is expressed in oriented source pixels and used directly by both the preview and export. Processing captures an immutable snapshot of each chosen rectangle. Resize scales that region to the target dimensions; crop-only keeps that region's approximate original pixel dimensions, rounded to whole pixels.

A regression test reproduced an earlier bug with 144-DPI images: the full-resolution loader used `DrawImageUnscaled`, which copies at [physical size based on DPI](https://learn.microsoft.com/en-us/dotnet/api/system.drawing.graphics.drawimageunscaled). That could shrink or enlarge the content inside its pixel canvas before cropping, producing results different from the preview. Image copies now specify both source and destination rectangles explicitly in pixels. Tests compare rendered previews and exported pixels across 72/96/144/300 DPI, all four manual rotations, and crop-only/resized output.

## Build and verify

From Windows PowerShell in this directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

Uses the C# compiler bundled with Windows .NET Framework. No SDK or NuGet restore needed. Source is in `QuickCrop.cs`. Tests exercise crop bounds/aspect ratios, preview/export correspondence, session autosave and reopening, changed-source handling, atomic backup and failed-write safety, Unicode filenames, image export and collision safety, all eight EXIF orientations, thumbnail batch failures, rotation, and both light/dark rendering and text contrast. Test binaries and screenshots are in `dist/checks`. If `QuickCrop.exe` is running during a build, the portable update is written to `dist/QuickCrop-updated.exe`, leaving the running app open.

Run `dist\checks\Tests.exe --benchmark` after building tests to compare the original full-resolution thumbnail path against the new decoder and bounded parallel loading. A local run with twelve generated 6000 x 4000 JPEG images took 9.36 s with the original path, 2.16 s with the new single-worker path, and 0.73 s with four workers (12.7x faster). This is a warm-filesystem synthetic decode benchmark, excluding card layout; actual times depend on image format, storage, and hardware. Folder loading displays its elapsed time in the status bar.

## Design and limits

Windows Forms, System.Drawing, and Windows' WIC decoder (through the bundled .NET Framework WPF imaging assemblies) keep the portable executable small without third-party dependencies. Previews decode directly at a maximum of 720 pixels with up to four workers. Full-resolution copies are reserved for export. Cards are added in batches to avoid a layout pass per file, and dominant-color detection is computed once per preview. Some image codecs still need full source decoding internally. Loading and exporting happen off the UI thread. Cards remain in memory for the current folder, so very large folders (thousands of images) can consume significant memory; virtualized cards and a bounded thumbnail cache would be the next scaling improvement.

The app reads Windows' app light/dark preference at startup and responds to theme-change messages during use. Text, cards, buttons, dropdown items, and numeric inputs receive explicit contrasting foreground/background colors. Windows high-contrast colors take precedence. Theme detection reads the registry without changing it. See Microsoft's documentation for [thumbnail-sized decoding](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/how-to-use-a-bitmapimage) and the [Windows app theme setting](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-common).

Closing during image loading cancels without saving partial state; export still must finish before closing. This version does not support RAW/HEIC/WebP, preserve EXIF/ICC metadata, or perform color-managed rendering. Export is re-encoded rather than lossless JPEG cropping. Crop-only aspect ratios can differ slightly due to whole-pixel rounding. Target dimensions allow up to 16384 per side, with a 64-megapixel output limit; inputs are limited to 100 megapixels. Large exports still require sufficient RAM. The executable is unsigned. Simultaneous instances do not merge session edits. The [review](docs/REVIEW.md) lists remaining limits candidly.

Useful next additions: export cancellation, virtualized cards for huge folders, keyboard navigation, an export-format selector, and a larger single-image editing view.

## License

[MIT](LICENSE). Copyright (c) 2026 Pablo Rivas.
