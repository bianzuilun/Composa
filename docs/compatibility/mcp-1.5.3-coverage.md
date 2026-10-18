# MCP coverage for the Compositor 1.5.3 port

The initial Composa 1.5.3 release added editor features while retaining an older MCP surface. This follow-up connects the missing workflows; it does not retroactively change the published installer. `get_capabilities` reports the running editor's surface, and MCP `tools/list` is the source of exact parameter schemas.

| Ported area | MCP route |
| --- | --- |
| Liquify, sharper source sampling | `liquify`; `paint_stroke` / `paint_strokes` with `mode=liquify` |
| Texture-aware spot healing / fill | `paint_stroke` / `paint_strokes` with `mode=heal`; `content_aware_fill` |
| Scaled Clone Stamp, stable Smudge, independent Blur radius | `clone_stroke`; `mode=smudge`; `blurRadius` in both stroke APIs |
| Gaussian transparency preservation | `filter_blur(preserveTransparency=true)` |
| Progressive Blur / draggable endpoints | `filter_progressive_blur` with canvas endpoints and start/end radii |
| Independent Scanlines / CRT controls | `filter_scanlines`, including glow, dots, wobble, displacement, smoothing, threshold, split and black level |
| Repeat last accepted filter | `repeat_last_filter`; UI and MCP share accepted filter state |
| Complete Camera Raw, measured color, regional Shadows | `filter_camera_raw(settings=...)`; all nested groups are available in the input schema |
| Curves, parametric regions, mixer, grading, detail, optics, calibration | Nested Camera Raw settings; `get_camera_raw_settings` supplies the complete defaults or last accepted grade |
| Camera Raw group reset / visibility | `resetGroups` / `disabledGroups`; reset clears the full group, disabled groups are excluded from the applied grade |
| Native 16-bit / HDR composition, adjustments and persistence | Existing adjustment/save/render paths retain precision automatically; `describe_document` / `get_layer_details` report depth |
| Layer 8/16-bit conversion | `convert_layer_depth`; conversion is undoable, HDR-to-integer conversion clips to SDR |
| 16-bit PNG/TIFF, HDR EXR, RGB TIFF/PDF, sized export | `export_image` with `bitDepth`, dimensions and path format; export snapshots the document |
| CMYK profile, proof/gamut and CMYK JPEG/TIFF/PDF | `set_print_proof`; `render_view`; `export_image(colorMode=cmyk)` |
| PSD/PSB RGB/CMYK, native high-depth imports | `inspect_import`, `open_document`, `place_image`; conversions require explicit `allowConversions` |
| RAW exposure/WB/denoise/highlights/lens, then Camera Raw | RAW `rawSettings` plus `cameraRaw` in open/place/folder import; no modal sheet |
| HEIF/HIF, WebP, TIFF, PNG16, EXR/Radiance | Standard open/place paths use the same decoders as the UI |
| Folder import, file drops, canvas fitted to imported content | `import_images(paths=[...], newDocument=true)`; `place_image` for individual placement |
| Welcome-screen paste / precision-aware clipboard | `clipboard` with paste/copy/cut actions, using the same window paths |
| Selection / inverse masks, mask editing and solo preview | `layer_mask`, `render_mask`, existing selection APIs |
| Group / ungroup and layer action naming | `group_layers`, `ungroup_layers`, existing set/delete/reorder tools |
| Smooth Corners, fill-first shapes, live content / effect resize | `add_shape(smoothCorners=true)`, `set_shape`, `set_text`, `set_layer_effects`, `transform_layer`, image/canvas/crop/trim tools |
| Transform percentages | `transform_layer(widthPercent=..., heightPercent=...)`; calls commit immediately, so no pending transform needs Apply/Cancel |
| Physical canvas dimensions / DPI and background | `new_document` with `unit`, `resolution`, `background`; `canvas_size`; existing `image_size` |
| 90-degree canvas rotation | `rotate_canvas` |
| Histogram / vectorscope | `get_color_scopes` returns bins, sample counts and optional image |
| Command search and clear/rebind shortcuts | `list_commands`, `set_shortcut`; bindings persist and retain custom precedence |
| Canvas-only fullscreen / Navigator zoom | `set_workspace` with `canvasOnly`, `zoom`, `fit`, `navigator` |
| Tab ordering / active document | `set_workspace(document=..., position=...)`; `list_documents` reports resulting indices |
| Auto Select, transform controls, rulers/guides/snapping | `set_workspace` preferences |
| History inspection / single-restore scrubbing | `get_history`, `go_to_history`, `undo`, `redo` |
| Expandable Release body / own-repository updater | `get_update_notice` reads the existing notice without issuing network/download/install actions |
| System / English / Chinese / Japanese | `get_preferences`, `set_preferences`; language remains restart-applied |

GPU-assisted preview, bounded scope sampling, selected-layer reveal, narrow Layers layout, tab overflow/scrolling, option-bar scrolling, temporary modifier inversion and immediate cursors are rendering or pointer behaviors, not separate pixel operations. The corresponding MCP calls operate by coordinates and stable layer IDs rather than simulating mouse gestures. UI GPU eligibility/fallback is automatic; MCP applies a grade using the native-precision CPU path for a deterministic committed result. Branding, translated READMEs and release packaging are not editing tools.

See [client setup](../mcp-clients.md), [tool reference](../ai-tools-reference.md) and [the port audit](compositor-1.5.3-audit.md) for platform limitations. Unknown or unsupported parameters should be treated as errors by the client. A successful edit can be undone; preferences, view changes and file writes are not document-history steps.
