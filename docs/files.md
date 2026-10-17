# Files

## Opening

File > Open (Ctrl+O) opens one or more files. Each project opens in its own tab. Each image becomes a new, unsaved document with a single layer named after the file. A file that is already open just becomes the active tab. File > Open Recent lists the last twelve files you opened or saved.

Files can also be dropped onto the window. Dropped projects open in tabs. Dropped images are placed as layers into the document you are working on, centered where you dropped them if that is inside the canvas; with no document open, over the tab bar, or when projects are dropped at the same time, images open as documents instead. Pasting an image on the welcome screen creates a canvas for it.

### What opens

- **Composa projects**: `.cmps` files, with all their layers.
- **Images**: PNG, JPEG, WebP, BMP, GIF and ICO, plus HEIC, HEIF, AVIF and TIFF when ImageMagick is available. The orientation stored by a camera is honoured.
- **SVG**: drawn at the size the file declares. The result is pixels; it does not stay a vector drawing.
- **HDR**: OpenEXR and Radiance RGBE (`.exr`, `.hdr`). Native EXR decoding handles uncompressed/ZIPS/ZIP scanlines; other layouts require an HDRI-enabled ImageMagick, bundled on Windows.
- **Photoshop**: `.psd` and `.psb` files, 8/16-bit RGB and CMYK. Embedded ICC profiles are used for conversion. See [Photoshop files](#photoshop-files).
- **GIMP**: `.xcf` and `.xcf.gz` files from GIMP 2.10, 3.0 and 3.2, in any precision. See [GIMP files](#gimp-files).
- **Camera RAW**: DNG, CR2, CR3, NEF, ARW, RAF, ORF, RW2, PEF and most other RAW formats, when ImageMagick is available. See [Camera RAW files](#camera-raw-files).

A single image or layer can be up to 30,000 pixels on a side and 200 megapixels. A whole document has a budget for its layers that depends on the memory in your machine, between 200 and 800 megapixels, so a banner with many large layers opens as long as the machine can hold it.

## Placing

File > Place Images as Layers adds image files to the current document as layers, scaled down to fit the canvas if they are larger and centered, and switches to the Move tool so you can position them. An SVG placed this way is drawn to fit the canvas, so a small icon comes in sharp rather than enlarged. A Photoshop file placed into a document arrives as a folder named after the file, with its layers inside.

## Saving

File > Save (Ctrl+S) writes the project; the first time it asks where. File > Save As (Ctrl+Shift+S) writes a copy under a new name and the document continues from there.

A project is a `.cmps` file. It keeps the canvas size and resolution, every layer with its pixels, transform, mask, effects, live text and shape settings, adjustment layers with their settings, folders, the guides and which layer was active. It is a zip archive with a description and one image per layer, so nothing in it is secret.

Project format 7 keeps 16-bit sources as PNG and floating-point sources as EXR, together with the editing preview's changes. Older project versions still open. A layer's context menu converts between 8 and 16 bits; reducing depth is undoable but loses precision in the committed result.

Saving happens in the background. The document as it is when you press Save goes to disk while you keep working, and the status bar shows "Saving" with the file name until it is done. Only the state that was saved counts as saved: an edit you make meanwhile leaves the document modified. Closing a document or quitting waits for a save still in progress, so a file is never cut short.

Projects saved by the macOS app cannot be opened; Composa has its own format.

## Exporting

Exporting flattens the document to a single image and leaves the project as it is.

- **File > Export PNG** (Ctrl+Shift+E): lossless, with transparency.
- **File > Export JPEG** (Ctrl+Alt+Shift+S): shows a preview with a quality slider from 1 to 100, the image size and the resulting file size, and composites transparent areas over white. The quality you choose is remembered.
- **File > Export WebP**: uses the quality last chosen for JPEG.
- **File > Export As**: choose dimensions and PNG/JPEG/WebP/TIFF/PDF, 16-bit PNG/TIFF or HDR EXR. Preview shows the encoded size and, for JPEG/WebP, the compression result. PDF contains one page sized from the document's DPI.
- **CMYK export**: in Export As, choose JPEG, TIFF or PDF and enable CMYK. The output contains the print profile chosen with View > CMYK Profile (Coated FOGRA39 by default). Transparent areas become paper white. View > Proof Colors (Ctrl+Y) and Gamut Warning (Ctrl+Shift+Y) preview the print conversion without changing RGB document pixels. The profile choice is saved in Settings. These conversions require ImageMagick.
- **Save Look…** in the [Camera Raw Filter](camera-raw.md) writes that grade's color stages as a `.cube` in the same way.
- **File > Export Look as .cube**: bakes the document's adjustment layers into one 3D lookup table, at 17, 33 or 65 points, that any editor with a Color Lookup can load, so a look built here from Curves, Hue/Saturation and a Gradient Map can go to DaVinci Resolve or Photoshop. Only what changes a color by its color alone can go into a table: a layer with a mask, a clipped layer, a layer inside a folder, and Grain, Add Noise and the blurs are left out, and the dialog lists them before anything is written. The table is written at the layers' opacities, bottom to top, and reads back in Composa's own Color Lookup as the same look.

## Photoshop files

Composa reads Photoshop files and never writes them. A file opens as an unsaved document; save it as a Composa project to keep your work.

What survives: layers and folders, visibility, opacity and fill, masks, clipping, and blend modes (Dissolve, Darker Color and Lighter Color become Normal). Levels, Curves, Hue/Saturation, Brightness/Contrast, Exposure, Invert, Color Balance and Black & White adjustment layers arrive as Composa adjustment layers, and so does a Color Lookup made from a `.cube` or `.3dl` file, whose table Photoshop keeps inside the document. Horizontal text with one style arrives as editable text; vertical, sheared or unevenly scaled text becomes pixels. Solid fills and simple vector shapes become live shapes where possible.

What does not: layer effects are dropped, smart objects arrive as pixels, gradient and pattern fills arrive empty, and a Color Lookup through an ICC profile or a SpeedGrade `.look` is skipped. When anything has to be converted, an "Open" dialog lists what will change, layer by layer, before the file is opened; Import goes ahead. A file too large for memory has its layers cropped to the canvas rather than being refused, and the dialog lists every layer that was cut.

## GIMP files

Composa reads GIMP's `.xcf` files, and `.xcf.gz`, and never writes them. A `.xcf.bz2` or `.xcf.xz` is refused with a message; save it from GIMP as `.xcf` or `.xcf.gz`. A file opens as an unsaved document; save it as a Composa project to keep your work. Dropped onto an open document, a GIMP file arrives inside a folder named after it.

What survives: layers and folders (folded as they were), offsets, visibility, opacity, masks and whether they apply, guides and the resolution, blend modes where the math agrees here, including GIMP's older set from before 2.10, and text in one style, which can be retyped: its wording, font, size, color, alignment, box and spacing come across. Grayscale and indexed files become RGB. Every precision opens, 8 to 32 bits per channel and 16 to 64-bit floating point, linear or not; the pixels become Composa's 8-bit sRGB and a deep file is reported as having lost precision.

What does not: text that mixes fonts or colors arrives as pixels, GIMP 3's layer effects (its non-destructive filters) are dropped and the layer shows as GIMP would with them switched off, vector and link layers arrive as pixels, saved channels, paths and color profiles are left out, and the painting modes (Dissolve, Behind, Color Erase, Erase, Merge, Split) become Normal. GIMP blends most layers in linear light where Composa blends in sRGB, which makes no difference to a Normal layer at full opacity but a little to the others, so those are listed. As with Photoshop files, an "Open" dialog lists every conversion, layer by layer, before anything is opened; a file that needs none opens straight away. A file too large for memory has its layers cropped to the canvas rather than being refused.

## Camera RAW files

A RAW file opens through a "Develop" dialog with Exposure (in EV, from -3 to 3), Temperature, Tint, luminance/chroma noise reduction, highlight recovery, manual radial lens correction and Reset. Import develops the whole frame with those settings, retaining 16-bit data. Continue in Camera Raw opens the [Camera Raw Filter](camera-raw.md) for finer control afterwards. Development uses LibRaw's decoded image rather than sensor data; lens correction does not supply automatic camera-specific profiles.

## Closing and quitting

Closing a tab or the window with unsaved changes asks "Save changes before closing?" with Don't Save, Cancel and Save. Quit is File > Quit (Ctrl+Q).
