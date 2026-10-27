# Adversarial code review

Reviewed 2026-10-27. This was an AI-assisted review of the source plus targeted local Windows regression tests. It was not an independent security audit or a fuzzing campaign against Windows' native codecs.

The review looked for ways to lose saved edits, make the preview and export diverge, block normal editing, and exhaust resources. The earlier DPI-copy bug is also retained as a regression test.

## Findings addressed

| Priority | Trigger and original failure | Change | Evidence |
| --- | --- | --- | --- |
| High | Two instances load the same session, make different changes, and save. The later save silently replaces newer work. | Hold a per-folder writer lease; additional instances have read-only sessions. Compare the loaded JSON revision before replacement to reject external edits too. | Two-window regression, writer-lock release/reacquire, and external-edit conflict tests. |
| High | Source changes after a thumbnail was decoded but before its card read the file's timestamp. An old preview could be attributed to the new source and exported. Export also checked the path before opening it again. | Capture metadata and identity under the same read handle used for decoding; validate export identity under its decode handle. Both handles disallow concurrent writers/deletion. | Mutate after thumbnail decoding and before card construction; export must reject. Also reject previews of a source held open for writing. |
| Medium | Width and height change separately. Each intermediate ratio resets crops, even when the final ratio equals the original. | Apply custom dimensions together using **Apply size**. Pending edits preserve framing and cannot be exported. Presets still apply immediately. | Set 1920x1080 to 3840x2160 through both fields; crop survives before and after application, and saved targets change only after Apply. |
| Medium | A JSON document is structurally readable but contains invalid crop entries or duplicate names. Entries were silently ignored/reset and a later autosave could rewrite that data. Very large JSON was read before its size was checked. | Validate individual entries, finite bounds, rotations, duplicate names, filename shape, and size; preserve invalid files and disable writes. Bound file size before reading and before saving. | Zero-width/out-of-bounds crops, invalid rotation, duplicate/path-like names, and oversized-file regressions. Existing corrupt-file and backup tests retained. |
| Medium | A very large source/target can request huge bitmap allocations; integer multiplication can overflow a naive pixel-count check. | Validate source and output dimensions with 64-bit arithmetic before creating output bitmaps; cap sources at 100 MP and outputs at 64 MP. | Oversized target, integer-overflow, and invalid-crop tests reject before allocation. Codec internals can still require memory before a complete header is available. |
| Medium | Bulk selection runs the per-card status recount and autosave scheduling for each checkbox, causing quadratic work in large grids. | Suppress per-card callbacks while bulk-selecting, then refresh/schedule once. Added **Select all** and **Select none**, preserving framing. | Actual toolbar-button tests verify checkboxes and unchanged crop rectangles. |
| Medium | Export writes directly to its final filename. Termination during encoding can leave a truncated image appearing finished. A directory with that filename also prevented normal collision handling. | Encode to a unique temporary file, flush/close it, and move to an unused final name without replacing existing files or directories. | Existing collision tests, directory-name collision, and successful staging-file cleanup. Abrupt termination itself is not fault-injected. |
| Low | Merely hovering over a preview calls Focus, taking focus from the numeric inputs and potentially validating an unfinished edit. | Remove focus-on-hover; route wheel messages to the hovered crop through a scoped message filter. | Hover regression keeps numeric-input focus; existing wheel interaction checks retained. Native message routing still benefits from manual testing on different Windows settings. |
| Low | Drawing a full bitmap or adding/restoring a card fails after allocation; allocated bitmaps/cards could escape deterministic cleanup. | Dispose failed full-image copies and failed cards while keeping thumbnail ownership explicit. | Exception-path source inspection; no forced GDI allocation-failure test. |

## Additional validation

The test host now runs a real Windows Forms message loop and reports failures through its exit code. This avoids a test-harness artifact where async continuations in a second window could run without the expected UI synchronization context. The failure was in the previous harness setup; it was not reproduced in the app's normal `Application.Run` entry point.

The full local suite includes the existing preview/export pixel tests, EXIF/DPI cases, autosave and normal close, theme contrast, and generated geometry cases. See `Tests.cs` and `ReviewTests.cs` for executable scenarios.

## Remaining limits and improvements

- **Scaling:** every card/thumbnail stays in memory. The worker pool bounds concurrent decodes, not the entire folder. A virtualized grid and bounded thumbnail cache are the next substantial performance change.
- **Cancellation:** native decoding/encoding and batches have no Cancel control. The current UI waits for completion before closing. Adding cancellation between files and a graceful deferred close is still useful work.
- **Resource limits:** 100 MP / 64 MP caps reduce avoidable allocations but do not guarantee a batch fits available memory, particularly for formats that decode fully inside native codecs.
- **Source identity:** size and timestamp detect ordinary changes, not deliberately timestamp-preserving edits. Hashing full images would be stronger but would add significant folder I/O.
- **Storage:** session backup/replacement and output moves use Windows filesystem guarantees. Network shares and removable filesystems may behave differently; simultaneous writes by unrelated tools can still race the final revision check.
- **Session recovery:** a valid `.bak` is available for manual recovery. There is no UI to merge conflicting sessions or recover a backup automatically. Only one writer is supported per folder.
- **UI/accessibility:** native control decoration, high-DPI layouts, keyboard navigation, screen-reader descriptions, and high-contrast mode need broader manual coverage.
- **Image support:** no RAW/HEIC/WebP, color-managed rendering, metadata preservation, lossless JPEG transforms, or multipage/frame selection.

No claim is made that this review found every defect. These remaining items are documented improvement opportunities, not features implemented by this review.
