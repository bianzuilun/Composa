# AI control

Composa can be driven by an AI agent. The agent works in the documents you have open, through the same commands you use: every change it makes is one undoable step, it shows up in the window as it happens, and Ctrl+Z takes it back like anything you did yourself. You keep working alongside it in the same window.

This is what makes the feature safe to try. The agent never touches your files behind your back; it edits what you see, and you can watch, stop and undo.

## Switching it on

Tick Help > Allow AI Control. It is off by default and remembered between launches. While it is on, Composa listens for agents on a private connection that only your own user account on this machine can reach; nothing is opened to the network. The status bar shows "AI connected" while an agent is attached, or how many are.

Only one Composa window at a time can accept agents. If you open a second window it says so, and agents keep working in the first.

## Connecting a client

Composa speaks the [Model Context Protocol](https://modelcontextprotocol.io) (MCP), which is how AI assistants are given tools. Any MCP client can connect: Claude Code, Claude Desktop, or another assistant that supports MCP servers.

An MCP client starts a small program and talks to it. For Composa that program is `composa --mcp`, a bridge that carries the client's messages to the running Composa. Register it with your client as a command:

- **Command**: the path to the `composa` executable. On Linux with a package installed that is just `composa`; with the AppImage or the tarball, the full path to the file. On Windows, `composa.exe` in the folder where Composa is installed.
- **Arguments**: `--mcp`.

For Claude Code, from a terminal:

```bash
claude mcp add composa -- composa --mcp
```

Other clients take the same command and argument in their own configuration. See [Codex, DeepSeek Harness 0.2.1-rc.2 and Claude setup](mcp-clients.md) for client-specific examples.

### The bridge

The bridge outlives Composa. While Composa is not running, or AI control is off, the client sees no tools and any call it makes is answered with a message saying so. The moment Composa starts with AI control on, the tools appear, so you can start, quit, update and restart Composa without touching the client. A request that arrives while the connection is being made waits for it, and a request that was in progress when Composa quit gets an answer instead of hanging.

Add `--launch` after `--mcp` in the registration and the bridge starts Composa itself when nothing answers, once per session. If you quit Composa later, the bridge leaves it closed.

Some clients only read the tool list when they start. If a client shows no tools after Composa started, restart the client once.

## What an agent can do

Call `get_capabilities` for the running editor's editing routes and `tools/list` for exact schemas. The tools cover these workflows:

- **Documents**: create a canvas, open a project or image, list and describe the open documents, save the project, export a PNG, JPEG or WebP, and render the document to see it.
- **Layers**: add a layer, place an image or SVG file as a layer, select, rename, hide, reorder, duplicate and delete layers, move, resize and rotate them, and set opacity and blend mode.
- **Content**: add text with a font, size, color, bold and italic; add rectangles, rounded rectangles, ellipses and lines; fill a layer; paint brush strokes with the brush, eraser, blur, smudge, dodge and burn, one at a time or many in one call.
- **Adjustments**: every adjustment, on the layer's pixels or as an adjustment layer.
- **Filters**: Camera Raw with all its groups, Progressive Blur, Scanlines, Dither, Painterly and the other filters; repeat the last accepted filter.
- **Selections**: marquee, lasso, wand, object and subject; select all, inverse, deselect; expand, contract, feather and move the selection.
- **Looking**: render the document, with a labelled grid to read coordinates from or a region at full size; read the colors at points; and trace the picture's edges.
- **Undo**: take back the last step, whoever made it.

The document list, a document's layers and its rendered image are also available as resources, for clients that attach context rather than call tools. The full list of tools and their parameters is in the [AI tool reference](ai-tools-reference.md).

Photoshop/PSB, GIMP conversions and RAW development are available without modal dialogs. `inspect_import` lists conversions before an explicit `allowConversions=true` import. RAW takes develop settings and an optional complete Camera Raw grade. Export supports native 16-bit PNG/TIFF, HDR EXR, TIFF/PDF and CMYK print formats. Liquify, healing, mask editing, live shape/text/effects, tab/view preferences and shortcut rebinding are also exposed. See the [1.5.3 coverage map](compatibility/mcp-1.5.3-coverage.md).

An edit is refused while you are dragging on the canvas or have a pending preview. Preferences/view changes persist separately from document history; file writes require the same explicit overwrite choice as other MCP save/export calls.

## What to ask for

Agents do best with work you could explain to a colleague over your shoulder: put this logo bottom right at a fifth of the width with a drop shadow; remove the background and put the product on a gradient; brighten the shadows, add a vignette and export a WebP; lay out a card with this title in this font; apply the same treatment to these ten files. Name the layer or the file, say where things should go, and let the agent render to check its work.

Two kinds of request work less well. Asking an agent to draw something recognizable from scratch with brush strokes rarely produces a likeness, because a language model estimates where things are from a picture and is off by tens of pixels. For a hand-drawn look, ask for the Painterly filter on a photo, which paints from the pixels; or ask the agent to place the photo as a reference and use the edge tracing and color sampling tools, which give it the picture's real lines and colors to work from. And a task that needs your eye, such as retouching a face, is better done in steps you review with a render between them.

## Privacy

The connection between the client and Composa stays on your machine: it is a named pipe on Windows and a socket file in Composa's cache folder on Linux, both readable only by your own user account. What the agent sees of your document, and where that goes, depends on the AI client you use and its provider; Composa itself sends nothing anywhere.
