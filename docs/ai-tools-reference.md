# AI tool reference

Every tool an agent gets, with its parameters. Coordinates are canvas pixels with the origin at the top left. Colors are written as `#rrggbb` or `#aarrggbb`. Pixel/layer edits use document history; view preferences and file writes do not. For exact input schemas use MCP `tools/list`, and for version-specific availability call `get_capabilities`.

Most tools take a `document` parameter: the tab number as `list_documents` reports it. Leave it out for the active document. Tools that work on a layer take `layer`: the layer's name, or the eight-character id that `describe_document` shows in brackets when two layers share a name. Leave it out for the active layer.

The [1.5.3 coverage map](compatibility/mcp-1.5.3-coverage.md) maps every ported feature to its MCP route. [Client setup](mcp-clients.md) covers Codex, official DeepSeek Harness 0.2.1-rc.2 and Claude.

## Extended workflows

| Tool | Parameters and behavior |
| --- | --- |
| `get_capabilities` | Actual tool names and editing routes for the connected editor. |
| `liquify` | `points`, `size`, `strength`, `hardness`, `layer`, `document`; at least two different canvas points. |
| `clone_stroke` | `points`, `sourceX`, `sourceY`, brush size/hardness/opacity and `sampleAllLayers`; preserves interactive stamp settings. |
| `draw_gradient` | Canvas `startX/startY/endX/endY`, start/end colors, `radial`, `toTransparent`, `opacity`; selection and mask aware. |
| `content_aware_fill` | Target `layer` / `document`; requires a selection on raster pixels. |
| `filter_progressive_blur` | Canvas endpoints and `fromRadius` / `toRadius` (0..250). |
| `filter_scanlines` | `spacing`, `thickness`, `glow`, `dots`, `wobble`, `displace`, `smoothness`, `threshold`, `split`, `density`, `contrast`, `blackLevel`, `colors`, `dark`, `light`. |
| `filter_camera_raw` | Complete `settings` record, optional `resetGroups` / `disabledGroups`, seed and target. Every nested group is in the schema. |
| `get_camera_raw_settings` | `last=false` returns complete defaults; `last=true` returns last accepted UI/MCP grade. |
| `repeat_last_filter` | Applies the last accepted UI/MCP filter to the target layer. |
| `convert_layer_depth` | `depth` 8 or 16; raster target only. |
| `layer_mask` | `action`: add/enable/disable/delete/apply/edit/pixels/solo/unsolo; `inverse` on add uses the inverse selection. |
| `render_mask` / `render_view` | Mask coverage or print-proofed document image; optional output `maxSide`. |
| `set_print_proof` | Optional `proofColors`, `gamutWarning`, `profilePath`, `document`; settings persist without changing pixels. |
| `get_color_scopes` | Optional `layer`, `includeImage`, `document`; histogram bins and vectorscope samples/image. |
| `inspect_import` | Absolute `path`; dimensions/precision and conversion report without document edits. |
| `import_images` | File/folder `paths`, `newDocument`, conversion acceptance and RAW/Camera Raw settings; reports per-file failures. |
| `group_layers` / `ungroup_layers` | Group named sibling `layers`, optionally name the folder; ungroup by folder `layer`. |
| `canvas_size` | Width/height, physical `unit`, anchor; no resampling. |
| `crop_canvas` | x/y/width/height in canvas pixels; keeps live content. |
| `trim_canvas` | `basedOn`, tolerance and four edge toggles. |
| `rotate_canvas` | Clockwise `degrees`: 90/180/270 (or -90). 180 degrees performs two quarter-turn history steps. |
| `set_layer_effects` | Layer plus full live `effects` record; empty record removes effects. |
| `set_text` | Layer plus only text/size/color/font/bold/italic fields to change. |
| `set_shape` | Layer plus fill `color`, `cornerRadius`, `smoothCorners`. |
| `get_layer_details` | Exact transform, depth, mask state, text/shape/effects records. |
| `set_workspace` | Activate/reorder a tab; set zoom/fit/canvasOnly, Auto Select, transform controls, pixel grid, navigator, rulers, guides and snapping. |
| `list_commands` / `set_shortcut` | Search palette command IDs; rebind/clear/reset a shortcut and persist it. |
| `get_preferences` / `set_preferences` | Read preferences; save system/en/zh-CN/ja language for restart. |
| `get_update_notice` | Current update phase, message and expanded Release body; no network action. |
| `get_history` / `go_to_history` / `redo` | Read step indices, restore a step with one restore, or redo. |
| `clipboard` | copy/copy_merged/cut/paste/paste_in_place/paste_into; active tab, including paste on welcome. |

Existing `paint_stroke` / `paint_strokes` also accept `liquify` and `heal`, plus independent `blurRadius`. `add_shape` accepts `smoothCorners`. `transform_layer` accepts `widthPercent` / `heightPercent` (source-pixel percentages). `export_look` additionally accepts a complete `cameraRaw` record to bake its color-only stages; spatial/adaptive controls omitted from a LUT are reported.

## Documents

**list_documents**: the documents open in Composa, numbered as their tabs are, with their size, layer count and whether they have unsaved changes. No parameters.

**describe_document**: the canvas size and resolution, the selection's bounds, and the layer stack top layer first, with each layer's kind, id, position and size, visibility, opacity, blend mode, mask, clipping and effects. Parameters: `document`.

**new_document**: creates a document in a new tab and makes it active. Parameters: `width`, `height`; `unit` (pixels, inches, centimeters, millimeters); `resolution` in pixels/inch; `background` as a color, left out for a transparent canvas.

**open_document**: opens a project/image/PSD/PSB/XCF/RAW in a new tab, or activates an already-open project. `path` is absolute. `allowConversions` accepts conversions listed by `inspect_import`; `rawSettings` contains exposure, temperature, tint, luminance/color noise reduction, highlight recovery and lens distortion; optional `cameraRaw` applies the complete Camera Raw grade after development. No modal dialog is opened.

**save_document**: saves the project with all its layers, in the background as Ctrl+S does. Parameters: `path`, absolute and ending in `.cmps`, left out to save to the document's own file; `overwrite`, needed to replace an existing file at a new path.

**export_image**: exports a snapshot to PNG/JPEG/WebP/TIFF/PDF/EXR by `path` extension. `bitDepth=16` selects native PNG/TIFF; EXR retains floating HDR. `colorMode=cmyk` selects 8-bit JPEG/TIFF/PDF with the current or `profilePath` ICC profile. `width`/`height` resize the export only (one dimension keeps aspect ratio); `quality` 1..100; `overwrite` is required for an existing file.

**export_look**: bakes the document's visible adjustment layers into a `.cube` lookup table at `path`, as File > Export Look does. Only what is a function of a pixel's color goes in; a masked, clipped or grouped layer and grain, noise and blurs are left out and named in the result. Parameters: `path` ending in `.cube`; `size` of 17, 33 or 65 points, 33 by default; `overwrite`.

**render**: the document as it looks now, as a PNG, so the agent can see the result of its work. Parameters: `maxSide`, the longest side in pixels, 1024 by default, never larger than the document; `grid`, a spacing in canvas pixels for a labelled grid over the picture, 0 for none; `x`, `y`, `width` and `height` for a region to render instead of the whole canvas, at full size up to `maxSide`.

**undo**: takes back the last step in the document, whoever made it. Parameters: `document`.

## Layers

**new_layer**: adds an empty, transparent layer the size of the canvas above the active layer and makes it active. Parameters: `name`, left out for the next free "Layer n".

**place_image**: places an image file as a layer above the active one. PNG, JPEG, WebP, BMP, GIF, SVG, and HEIC, AVIF or TIFF when ImageMagick is available. Parameters: `path`; `x` and `y` for the center of the image, left out to center it on the canvas; `fit`, true by default, scales the image down to fit the canvas but never up; `scale` multiplies the placed size, so 0.5 places it at half the size it would otherwise get.

**select_layer**: makes a layer the active one. Parameters: `layer`.

**set_layer**: changes a layer's `name`, `visible`, `opacity` from 0 to 1, or `blend` mode named as the Layers panel names it. Give only what should change.

**transform_layer**: moves, resizes or rotates a layer by setting its frame. Parameters: `x` and `y` for the left and top edge, `width`, `height`, and `rotation` in degrees, positive clockwise. Give only what should change. Text and shapes are redrawn sharp, and consecutive calls on one layer fold into one undo step.

**duplicate_layer**: copies a layer, or a folder with everything in it, right above the original; the copy becomes active.

**delete_layer**: deletes a layer, or a folder with everything in it.

**reorder_layer**: moves a layer among its siblings. Parameters: `direction`, one of up, down, top or bottom.

## Text, shapes and painting

**add_text**: adds a text layer. Parameters: `text`, where a newline starts a new line; `x` and `y` for the top-left corner; `size` in pixels, 72 by default; `color`; `font`, left out for the default; `bold` and `italic`.

**fill_layer**: fills the active layer with a color, within the selection when there is one. A text layer is recolored instead. Parameters: `color`.

**add_shape**: adds a live rectangle, rounded rectangle or ellipse as a new layer. Parameters: `kind`, one of rectangle, rounded or ellipse; `x`, `y`, `width` and `height`; `color`; `cornerRadius` for a rounded rectangle, 24 by default.

**add_line**: adds a live straight line with round ends as a new layer. Parameters: `x1`, `y1`, `x2`, `y2`; `color`; `width` in pixels, 4 by default.

**paint_stroke**: paints one brush stroke through the given points on the active layer, or on the layer named without changing which layer is active. Parameters: `points` as `[[x, y], [x, y], ...]`, where a single point is a dab and a curve needs a point every few pixels; `color`; `size`, the diameter, 40 by default; `hardness` from 0 (soft) to 1 (hard), 0.8 by default; `opacity` from 0 to 1, the most the stroke covers, or the strength for blur, smudge, dodge and burn; `mode`, one of paint, erase, blur, smudge, dodge or burn; `layer`. Live text and shapes cannot be painted on.

**paint_strokes**: paints up to two thousand strokes in one call as one undoable step, in the order given. Parameters: `strokes`, a list where each entry has `points`, `color`, `size`, `hardness`, `opacity` and `mode` as paint_stroke takes them; `layer`.

## Looking

**sample_color**: the colors at points, each averaged over a small disc. Parameters: `points` as `[[x, y], ...]`, up to five hundred; `radius` of the disc from 0 to 50 pixels, 2 by default; `layer`, to read that layer's own pixels rather than what is on screen, so a photo under the agent's strokes can still be read.

**trace_edges**: the edges in the picture as polylines in canvas pixels, longest first, so line work can follow where a face, an object or a fold really is. Parameters: `detail` from 0 to 100, how faint an edge may be, 50 by default; `minLength`, the shortest edge kept in pixels, 20 by default; `simplify`, how far a polyline may stray from the edge in pixels, 2 by default, where higher means fewer points; `maxLines`, at most how many edges come back, 150 by default and up to 1000; `layer`, to trace that layer's own pixels; `x`, `y`, `width` and `height` for a region.

## Selections

The selection tools take `mode`: replace (the default), add, subtract or intersect, as Shift, Alt and Shift with Alt do with the marquee. Where a `feather` is taken, it softens the edge by that many pixels.

**select_shape**: selects a rectangle or an ellipse given by `x`, `y`, `width` and `height`, or a polygon given by `points` with at least three. Parameters: `kind`, one of rectangle, ellipse or polygon; `mode`; `feather`.

**select_wand**: selects the pixels of a similar color around a point. Parameters: `x` and `y`; `tolerance` from 0 to 255, 32 by default; `contiguous`, true by default; `allLayers`, true by default, samples every visible layer rather than the active one; `mode`.

**select_object**: selects the object under a point, the connected piece of the subject there with its soft edge; with `width` and `height`, `x` and `y` are the top left of a box and the model runs on the box alone, selecting everything it finds inside, which suits a small object in a large picture. Parameters: `x` and `y`; `width` and `height`; `allLayers`; `mode`; `detect`.

**select_subject**: the subject of the whole picture. Parameters: `mode`; `detect`.

Both take `detect`: `any` runs the U²-Net model on the machine Composa runs on, for any subject; `person` runs MODNet, for people with soft hair; `plain` takes everything that is not the near-uniform backdrop touching the picture's edges, which is fast and exact on product shots and defeated by busy backgrounds. Left out, the Detect choice in Composa's Object Selection options applies. A model that is not available falls back to `plain`.

**select_color_range**: every pixel near the given colors anywhere in the picture, as Select > Color Range does. Parameters: `colors`, a list of colors as `#RRGGBB` or names; `exclude`, colors to leave out; `fuzziness` from 0 to 200, 40 by default; `invert`, to select everything else; `mode`.

**select_layer_pixels**: selects the shape of a layer's pixels, or of its mask with `fromMask`. Parameters: `layer`; `fromMask`; `mode`.

**select_all**, **deselect** and **select_inverse**: select the whole canvas, drop the selection, or select what was not selected.

**modify_selection**: changes the selection; several parameters apply in this order. Parameters: `expand` and `contract` in pixels, up to 500; `feather` up to 250; `moveX` and `moveY` in pixels.

## Adjustments

Each adjustment tool takes `asLayer`, which adds an adjustment layer above the layer instead of changing its pixels, and `layer`. Values are the same as in the dialogs.

**adjust_brightness_contrast**: `brightness` and `contrast`, each from -100 to 100.

**adjust_exposure**: `exposure` in stops, `offset` for the shadows from -0.5 to 0.5, and `gamma`, where 1 is unchanged.

**adjust_hue_saturation**: `hue` from -180 to 180, `saturation` and `lightness` from -100 to 100, `range` as master or one of reds, yellows, greens, cyans, blues or magentas, and `colorize`, which tints the whole layer with the hue from 0 to 360.

**adjust_levels**: `inputBlack` and `inputWhite` from 0 to 255, `gamma` for the midtones, `outputBlack` and `outputWhite`, and `channel` as rgb, red, green or blue.

**adjust_curves**: `points` as `[[input, output], ...]` from 0 to 255, and `channel`. `[[0,0],[255,255]]` is a straight line; `[[0,0],[64,48],[192,208],[255,255]]` adds contrast.

**adjust_black_and_white**: `reds`, `yellows`, `greens`, `cyans`, `blues` and `magentas` from -200 to 300, with the usual defaults when left out; `tint`, with `tintHue` from 0 to 360 and `tintSaturation` from 0 to 100.

**adjust_color_balance**: `shadows`, `midtones` and `highlights`, each three values from -100 to 100 as `[cyan/red, magenta/green, yellow/blue]`, where negative goes to the first color; `preserveLuminosity`, true by default.

**adjust_gradient_map**: `shadows` and `highlights` colors, black to white by default, and `reversed`.

**adjust_invert**: no values.

**adjust_color_lookup**: grades the layer through a lookup table. Parameters: `look`, a bundled look's name (Fine Mono, Muted Chrome, Standard Slide or Vivid Slide) or an absolute path to a `.cube` or `.3dl` file; `amount` from 0 to 100, 100 by default. An adjustment layer made this way is named after the look.

## Filters

Each filter tool takes `layer`.

**filter_blur**: `radius` in pixels, 8 by default.

**filter_motion_blur**: `distance` in pixels, 30 by default, and `angle` from -180 to 180.

**filter_add_noise**: `amount` from 0 to 100, `gaussian` for a bell-shaped spread, and `monochrome`, true by default.

**filter_sharpen**: `amount` from 0 to 100, 60 by default, over a `radius` in pixels, 2 by default.

**filter_vignette**: `amount`, `midpoint`, `feather` and `highlights` from 0 to 100, `roundness` from -100 to 100, and `color`, black by default. On an empty layer it fills the whole canvas.

**filter_bloom**: `amount` from 0 to 100 and a `radius` in pixels.

**filter_tonal_contrast**: `amount` from 0 to 100, a `radius` in pixels, and `shadows`, `midtones` and `highlights` from -100 to 100.

**filter_lens_correction**: `distortion` from -100 (pincushion) to 100 (corrects barrel distortion).

**enhance_layer_resolution**: Layer > Enhance Resolution for a raster layer shown larger than its own pixels: the model gives it pixels for its size on the canvas, up to four times what it has, and the layer keeps its place and size. Parameters: `layer`. Refused for text, shapes, folders, adjustment layers and layers shown at or below their size.

**image_size**: Image > Image Size. Parameters: `width` and `height` in pixels (one may be left out to keep the proportions); `resolution` in pixels per inch; `resample`: `automatic`, `nearest` for hard pixel blocks, or `enhance` to enlarge photo layers with a model run on the machine, which invents detail and takes about a second per 65,000 pixels of each layer. Where the model is not available, `enhance` resamples as `automatic` does and the result says so.

**filter_remove_background**: `detect` as for select_subject. With `any` or `person` the layer gets a mask hiding everything but the subject the model found, which can be painted on afterwards; with `plain` the backdrop is erased and `tolerance` from 0 to 100 says how different a pixel may be from it and still go.

**filter_painterly**: repaints the layer in brush strokes that follow the picture, as the Painterly filter does. Parameters: `style`, one of impressionist, expressionist, colorist_wash or pointillist; `brushSize`, the largest brush's diameter in pixels, 0 to fit it to the picture; `passes` from 1 to 4; `detail` from 0 to 100; `seed`, where the same seed paints the same strokes and 0 picks one.

**filter_dither**: turns the layer into dithered pixels, as the Dither filter does. Parameters: `style`, one of atkinson, floyd_steinberg, bayer2, bayer4, bayer8, halftone_dots, halftone_lines, halftone_diamonds, mac_patterns or ascii; `pixelSize` from 1 to 32 and `pixelShape`, square or dot; `cellSize` from 4 to 64 and `angle` from -90 to 90 for the halftone styles; `textSize` from 6 to 64 and `characters` for ascii; `tones` from 2 to 8 and `diffusion` from 0 to 100; `density` and `contrast` from -100 to 100; `colors`, one of black_white, two_colors or original, with `dark` and `light` for two_colors; `lightOnDark`, whether marks stand for the light tones.

## Resources

For clients that read resources, three are available: `composa://documents` gives the document list, `composa://documents/1` the layer stack of the first tab, and `composa://documents/1/image` its render as a PNG at most 1024 pixels on its longest side. Replace 1 with any tab number.
