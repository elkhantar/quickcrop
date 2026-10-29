# Changelog

## Unreleased

- Add global **Seam carving (experimental)**: remove low-energy seams from the whole oriented image to adjust aspect ratio, then optionally rescale to the target resolution.
- Hide manual crop boxes and **Fit** in seam mode while remembering manual framing; persist the mode per folder.
- Add carving progress and **Cancel processing**, preserving completed outputs and cleaning up cancelled temporary output.
- Add independent seam reference comparisons, whole-image/resizing/rotation/cancellation tests, and experimental-feature documentation.
- Allow immediate close during image loading, cancel remaining decode work, and discard partial edits without saving JSON or its backup.
- Align toolbar dropdown and dimension fields with button heights; keep disabled button text readable in dark mode.
- Keep disabled checkbox labels, ordinary labels, and dimension values readable during folder loading and export too.
- Add **Select all** and **Select none** without changing crop framing.
- Apply custom width/height together; preserve crops when the final ratio is unchanged.
- Prevent session overwrite by a second instance or after detected external JSON changes.
- Validate session entries and bound session file size before reading.
- Tie thumbnails and exports to file identity under a stable read handle.
- Stop hover from taking keyboard focus away from inputs.
- Guard oversized image/output allocations and invalid export rectangles.
- Publish complete encoded outputs through temporary files; handle directory-name collisions.
- Tighten bitmap/card cleanup on failures.
- Add adversarial regressions, a proper UI test message loop, usage/development documentation, and MIT licensing.

## Initial local versions

- Portable Windows Forms app with grid crop editing, rotation, wheel zoom, resize presets/custom sizes, and batch export.
- Replace slow full-image thumbnail copies with reduced-size WIC decoding and bounded parallel batches.
- Follow Windows light/dark app settings.
- Save per-folder selections, crop rectangles, rotation, and output settings with atomic JSON backups.
- Fix DPI-sensitive source copying so exported content matches the preview crop.
