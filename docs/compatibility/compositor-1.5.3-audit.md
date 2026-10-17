# Compositor 1.5.3 compatibility audit

The reference is [Compositor v1.5.3](https://github.com/robbietilton/Compositor/releases/tag/v1.5.3), commit `554104cf1c2de844c1acbd60b4f7b422bd6ce7bd`. The Composa starting point is localization release `v1.5.0-beta.2`, commit `2c0e3f11d341fc72176295455d9c1eeae4271f21`; its version did not imply compatibility with Compositor 1.5.

The original release bodies, tags, publication dates and source URLs are preserved in [compositor-release-bodies.json](compositor-release-bodies.json). All 14 releases after upstream `v1.4` through `v1.5.3` were read. Upstream's major tags are `v1.4` and `v1.5`, not `v1.4.0` and `v1.5.0`.

## Release-by-release changes

| Upstream release | Changes to account for |
| --- | --- |
| [1.4.1](https://github.com/robbietilton/Compositor/releases/tag/v1.4.1) | Resize snapping; immediate Move-bar edits with consolidated undo; scaled clone/blur sources; masks from selections, inverse and solo-mask gestures; ungroup; reordered/overflow tabs; temporary Auto Select inversion; paint failure hints; inverse of Select All clears the selection; transform percentages and Apply/Cancel context actions. |
| [1.4.2](https://github.com/robbietilton/Compositor/releases/tag/v1.4.2) | Sharper Liquify, less smudge ghosting and an independent Blur-tool radius. |
| [1.4.3](https://github.com/robbietilton/Compositor/releases/tag/v1.4.3) | Large-brush and large-image reliability and performance for smudge, blur and Liquify. |
| [1.4.4](https://github.com/robbietilton/Compositor/releases/tag/v1.4.4) | CRT-style scanline glow, dots and wobble; quitting safely with pending gradient or dialog work. |
| [1.4.5](https://github.com/robbietilton/Compositor/releases/tag/v1.4.5) | Draggable curve controls and gamma-based parametric curves; Camera Raw contrast also strengthens color. |
| [1.4.6](https://github.com/robbietilton/Compositor/releases/tag/v1.4.6) | Command search; canvas-only mode; physical canvas units, 72/300 DPI and white/black backgrounds; 90-degree canvas rotation; faster RAW import; translucent effects; color noise reduction. |
| [1.4.7](https://github.com/robbietilton/Compositor/releases/tag/v1.4.7) | Repeat Last Filter; F/Escape fullscreen; command search shortcut; layer actions/rename; horizontal options scrolling; Don't Save; closed-hand panning cursor. |
| [1.4.8](https://github.com/robbietilton/Compositor/releases/tag/v1.4.8) | Sized Export As previews and PNG/JPEG/single-page PDF; independent Scanlines with displacement, smoothing, dots, threshold, color split and black level; Navigator at 300%; reveal selected layer; monotone Camera Raw tone changes. |
| [1.4.9](https://github.com/robbietilton/Compositor/releases/tag/v1.4.9) | Photoshop-measured Camera Raw color math; faster preview; retain effects and editable text through image/canvas sizing, crop, trim and adjustments; WebP import; Ctrl+middle zoom; reset Untitled numbering; release-body display in the updater. The upstream speed multiplier needs an independent benchmark. |
| [1.4.10](https://github.com/robbietilton/Compositor/releases/tag/v1.4.10) | Rebuilt texture-aware healing/fill; folder import as layers; preserve-transparency Gaussian blur and solid canvas edges; RAW denoise, lens correction and highlight recovery; HIF/HEIF; narrow Layers layout; icon revision. |
| [1.5](https://github.com/robbietilton/Compositor/releases/tag/v1.5) | Preserve 16-bit PNG/TIFF/HEIF/RAW sources; 8/16-bit layer conversion; EXR/Radiance HDR import; EXR and 16-bit PNG export; CMYK proof/gamut, JPEG/TIFF/PDF export and PSD/JPEG/TIFF import; TIFF export; paste on welcome creates a canvas; tab-bar image drops create projects; imported layers determine the new canvas size; icon revision. |
| [1.5.1](https://github.com/robbietilton/Compositor/releases/tag/v1.5.1) | Progressive Blur with canvas control points; full-precision adjustment layers; smoother 16-bit/HDR display. |
| [1.5.2](https://github.com/robbietilton/Compositor/releases/tag/v1.5.2) | Regional adaptive Shadows; RAW-to-Camera-Raw workflow; group reset; clear/rebind shortcuts including print proofing and Delete; preserve custom bindings across updates; full-width update notice. |
| [1.5.3](https://github.com/robbietilton/Compositor/releases/tag/v1.5.3) | GPU-assisted full-resolution Camera Raw preview; faster histogram/scopes; continuous Smooth Corners; fill-first shape options; middle-button Layers panning; immediate zoom cursor updates. |

## Implementation map

These entries map the implemented paths. Verification below records the tested scope; it does not imply pixel-for-pixel parity with Apple's frameworks.

| Area | Composa implementation |
| --- | --- |
| Precision | `Rendering/DeepPixels.cs`, `Filters/DeepAdjustments.cs`, `Filters/DeepFilters.cs`, `Rendering/DocumentRenderer.cs`: immutable RGBA16/float source associated with an RGBA8888 editing copy; native float composition and spatial sampling; color reconstruction for legacy byte filters; selection/mask, copy/paste, geometric edits and project persistence. |
| Raster IO | `IO/Png16.cs`, `IO/ExrImage.cs`, `IO/RadianceImage.cs`, `IO/RasterExport.cs`: native 16-bit PNG including Adam7, scanline EXR, RGBE HDR, 8/16-bit TIFF and one-page RGB/CMYK PDF. Complex EXR layouts hand off to an HDRI decoder. |
| Photoshop | `IO/Psd`: 8/16-bit RGB and CMYK planes, ZIP prediction, high-depth layer blocks and ICC conversion. Layer structure and independent masks are retained. |
| Print color | `IO/Cmyk.cs`, `MainWindow.ColorProof.cs`: ICC selection, round-trip proof lattice, gamut warning and CMYK JPEG/TIFF/PDF. Print choices persist in Settings; documents retain their RGB content. Embedded CMYK PSD profiles can become the print profile. |
| Camera Raw color | `CameraRawTables.cs`, `CameraRawMeasuredColor.cs`, `CameraRawShadows.cs`, `CameraRawData/`: pinned measured tables and regional Shadows, composed float cubes, monotone curves and full group resets. Scene-adaptive/spatial stages are excluded from saved LUT looks. |
| Camera Raw preview | `CanvasView.CameraRaw.cs`: measured color cubes run on Skia's render canvas for eligible single-layer images; other cases use `PreviewFilterAsync`. The dialog coalesces changes and waits for a running preview before accepting. Histogram and vectorscope use bounded samples. |
| RAW development | `IO/RawImporter.cs`, `RawDevelopDialog.cs`: 16-bit development, exposure/white balance, luminance/chroma denoise, highlight recovery, radial lens correction and Continue in Camera Raw. |
| Healing and painting | `Painting/TextureInpaint.cs`: multiscale texture-aware PatchMatch and weighted votes, with seam adaptation. `BrushStroke.cs`: independent blur radius, stable smudge pickup origin and a sparse displacement field that samples Liquify from the untouched stroke source. |
| Filters | `SpatialBlur.cs`, `Scanlines.cs`: Progressive Blur and its draggable canvas endpoints, independent Scanlines controls, preserve-transparency Gaussian blur; the window remembers accepted settings for Repeat Last Filter. |
| Geometry/live content | Continuous shape corners and fill-first controls; resize snapping and percentage fields; nonuniform resizing retains a layer's quad instead of baking its effects or discarding editable text/shape data. |
| Workspace | Searchable commands, fullscreen canvas, Navigator, middle-button canvas zoom and Layers panning, selected-layer reveal, tabs/reordering/overflow, scrollable options, physical canvas units and DPI. |
| Masks and selection | Selection-derived masks, inverse mask creation, solo-mask preview, temporary Auto Select inversion, existing selection inversion and grouping behavior. |
| Files and preferences | Folder import, welcome paste, tab-bar drops, safe Untitled naming, rebindable print/Delete commands and persisted custom shortcut precedence. Update checks and downloads use this fork, with expandable release-body text. |
| Localization | Existing restart-applied system/English/Simplified Chinese/Japanese selection is retained. New controls use the same resource catalogs. English documentation and the two translated READMEs remain separate. |

## Cross-platform differences

Compositor uses AppKit, Core Image, Metal, Vision and Apple's camera pipeline. Composa uses Avalonia, Skia and LibRaw through ImageMagick; it does not invoke those Apple frameworks.

- GPU preview uses the host Skia renderer for supported single-layer color grades. Spatial effects, regional Shadows, masks/selections, print proofing and layered documents use the asynchronous precision-aware CPU path. No upstream performance multiplier is claimed.
- RAW lens correction currently exposes radial distortion manually. The LibRaw-based development and highlight reconstruction are equivalent editing controls, not a claim to reproduce Apple's camera-specific automatic lens profiles or sensor processing.
- Native EXR reading covers uncompressed/ZIPS/ZIP scanline half/float channels. Other EXR layouts require the bundled HDRI decoder on Windows or an HDRI-enabled system ImageMagick on Linux; an integer-only fallback is refused rather than clipping highlights silently.
- Composa retains its own application branding and icon. Upstream's icon replacement is an artwork change, not an editing capability.
- Some features already existed in the starting fork (WebP, canvas rotations, grouping, parts of selection handling). Release notes distinguish those from newly added work.

## Verification

- [Windows/Linux regression run](https://github.com/bianzuilun/Composa/actions/runs/39077904789): 414 Core tests and 303 headless App tests passed on each OS. The Linux configuration without bundled ImageMagick also compiled. [Package rehearsal](https://github.com/bianzuilun/Composa/actions/runs/39077904687) passed on Windows and Linux.
- Windows headless screenshots were inspected for Chinese tool groups, Japanese text, the depth badge, measured shader preview, RAW controls and expandable update notes. The Linux runner lacks CJK fonts, so its localization screenshots are not font-rendering evidence.
- Real exported fixtures from [the evidence run](https://github.com/bianzuilun/Composa/actions/runs/39078133942) were checked with Pillow/LittleCMS, tifffile, OpenEXR and PyMuPDF: CMYK JPEG/TIFF have four channels and a 654,352-byte ICC profile; the PDF renders its embedded ICCBased image on one DPI-sized page; 16-bit PNG/TIFF retain 32 adjacent sample values; EXR retains linear red 12.82984 (extended-sRGB red 3), beyond white.
- Precision regression tests cover native composition and adjustment layers, Gaussian blur, resampling, project/HDR round trips, independent PSD16 fixtures, RAW development, editable-content resizing, texture fill and asynchronous filter undo. Existing selection, text, model, update and localization suites remain enabled.

## Delivery requirements

The application, installer and GitHub release must all report `1.5.3`, using MinVer's draft-tag override and the eventual `v1.5.3` tag. No hand-edited application version is used.

Before publishing: run the Windows/Linux suites, inspect headless rendering evidence, validate actual exported files, build all twelve packages, verify `sha256sums.txt`, retain the Windows setup locally and read back the published release. Keep the release a draft until those checks succeed. The release body must describe actual changes and material platform limits.
