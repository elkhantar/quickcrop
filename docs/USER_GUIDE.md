# Using QuickCrop

## Start and choose images

Run `QuickCrop.exe` and click **Open folder…**. Supported inputs are JPEG, PNG, BMP, GIF, and TIFF. Only files directly in the chosen folder are loaded. GIFs and TIFFs use the first frame/page. EXIF orientation is applied before displaying or cropping.

You can edit cards as they finish loading. Folder and export controls become available when loading finishes. A corrupt or unsupported image is skipped and listed in a loading report; other images can still be processed.

## Choose an aspect ratio

A resolution preset applies immediately. For custom dimensions, edit both pixel fields and click **Apply size**. Pending custom edits do not affect crops or autosaved target settings. Processing stays disabled until those edits are applied. This prevents changing width and then height from resetting crops twice.

The ratio of width to height determines the rectangle shape. Applying a different ratio resets crop position and zoom on every card while preserving rotation and selection. Applying a different resolution at the same ratio keeps the existing framing.

## Frame each image

- Drag **inside** the red rectangle to reposition it. The shaded area outside it will be discarded.
- Wheel up over an image to shrink the crop, down to widen it. Zoom ranges from 1x to 20x; the widest crop is the largest rectangle that fits inside the source at the chosen ratio.
- Red-dominant images use a yellow outline for contrast.
- The left-arrow button rotates the source 90 degrees counterclockwise and resets its crop.
- **Fit** resets that card's crop and zoom without changing the target ratio.
- Editing a card selects it. Uncheck it afterwards if you do not want to export it.
- **Select all** and **Select none** affect checkboxes without changing any crop or rotation.

The image stays fitted within its card: wheel zoom changes the selected region, rather than enlarging the image across the card. **Card size** controls how large previews appear. Hovering an image does not steal keyboard focus from an input field.

## Crop or resize

**Resize output** unchecked: export just the chosen region at approximately its original pixel size, rounded to whole pixels. Very narrow ratios may differ slightly after integer rounding.

**Resize output** checked: export the chosen region scaled to exactly the target resolution. Small regions are upscaled when necessary. Regardless of this setting, the content inside the rectangle is the content exported.

Click **Process selected…**, choose an output folder, and wait for the completion report. Outputs are named `name_crop.ext`, `name_crop_1.ext`, and so on. Existing files, directories, and originals are not replaced. Images are encoded to temporary files before being moved to their final names. A process interrupted while encoding can leave an expendable `.quickcrop-export-*.tmp` file, but does not publish it under a finished image name.

PNG inputs produce PNG, preserving alpha. Other supported inputs produce quality-95 JPEG, flattening transparency onto white. Files are re-encoded; this is not lossless JPEG cropping. EXIF and ICC metadata are not copied to outputs, and rendering is not color managed.

Inputs are limited to 100 megapixels and requested output to 64 megapixels. Target fields allow up to 16384 pixels per side, subject to that total output limit. Limits reduce accidental large allocations but do not guarantee sufficient memory on every machine.

## Saved sessions

The input folder gets these small companion files:

| File | Purpose |
| --- | --- |
| `.quickcrop.json` | Selections, source-pixel crop rectangles, rotations, applied target dimensions, resize setting, and card size |
| `.quickcrop.json.bak` | Previous successful save |
| `.quickcrop.lock` | Empty file used to coordinate app instances; its existence alone does not mean the folder is locked |

Edits autosave after a short pause. The footer reports save success or failure. Pending edits are also flushed before folder switching, processing, and normal closing. **Apply size** must be used before custom numeric edits become part of the session.

Reopen the same folder to restore the session. Copy the JSON file with the images if moving them elsewhere. Entries use filenames, not absolute paths. Renamed or changed images start with a fresh crop and no selection. Source identity is checked again at export, and changed sources are rejected rather than silently using stale framing. Identity uses length and last-modified time; changes preserving both may not be detected.

Only one QuickCrop instance can save a folder at a time. Additional instances can view/edit/export but have a read-only session and cannot overwrite the first instance's data. If another program edits the JSON after loading, QuickCrop refuses to replace that newer version. Close and reopen the folder to load the newer saved data; unsaved edits must be discarded or preserved separately.

Malformed or oversized sessions are kept unchanged and autosave is disabled for that folder. To recover manually, close all instances, copy the current JSON somewhere safe, and copy `.quickcrop.json.bak` to `.quickcrop.json`. To start fresh, remove the JSON file while the app is closed. Do not delete the lock file while any instance has the folder open.

## Troubleshooting

**Can't save or switch folders:** check folder write permissions, disk space, and the footer message. A second instance or an external JSON edit can prevent saving. Closing with unsaved changes asks whether to discard them. The app never silently overwrites a conflicting session.

**Loading is slow:** local SSD storage is faster than network shares or removable disks. JPEGs can decode at reduced size; some formats still require a full internal decode. All cards stay in memory, so very large folders are best split into smaller batches.

**Need to stop a batch:** this version has no Cancel control. Closing while an operation is running asks you to wait. Keep batches manageable; cancellation and virtualized cards remain future work.

**Theme looks different:** the app follows the Windows app light/dark setting; high-contrast system colors take precedence. Native picker dialogs and some native control decorations still follow Windows rendering.
