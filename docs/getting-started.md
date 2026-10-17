# Getting started

## Installing

Every release on this fork's [releases page](https://github.com/bianzuilun/Composa/releases) comes in these forms:

- **Linux**: an AppImage, a `.deb` for Debian and Ubuntu, an `.rpm` for Fedora and openSUSE, and a plain tarball, each for x86-64 and arm64. The AppImage and the tarball run from wherever you put them. The packages install Composa like any other application and put it in your menu.
- **Windows**: an installer that needs no administrator rights, and a portable zip, each for x64 and arm64. The installer adds a Start menu entry, registers Composa for its own `.cmps` project files and offers itself under Open with for images without taking any of them over.

Composa opens HEIC, AVIF and TIFF images and camera RAW files through ImageMagick. The Windows builds include it. On Linux, install your distribution's ImageMagick package (`imagemagick` on Debian, Ubuntu and Fedora) and those formats open too; everything else works without it.

## The window

The window is laid out as image editors usually are.

- The **menu bar** holds every command, and most commands have a keyboard shortcut you can change (see [Keyboard shortcuts](shortcuts.md)).
- The **options bar** under the menu shows the settings of the current tool: brush size, marquee feather, text font and so on.
- The **toolbar** on the left holds the tools, with the foreground and background color swatches below them. A button with a small triangle in its corner holds a group of tools: press and hold it, or right-click it, to pick one.
- The **canvas** in the middle shows the document. Rulers can be shown around it.
- The **Layers panel** on the right lists the layers, top layer first, with their blend mode and opacity above the list. Under it the **History panel** lists the steps you can go back to; Window > History shows or hides it.
- The **status bar** at the bottom shows the zoom, the document's size and resolution, the pointer's position in canvas pixels, a hint for the current tool, and "AI connected" while an agent is working in the document.

With nothing open, the canvas area shows a welcome screen: "Create a canvas, open a project or image, or drop files here", with buttons for a new canvas and for opening files, and your six most recent files.

## Your first document

Choose File > New Canvas (Ctrl+N). The "New Canvas" dialog offers presets (4K, 1440p and 1080p; iPhone, MacBook Pro and Studio Display screens; Instagram Square, Portrait and Story and a YouTube thumbnail; A4 at 300 ppi and a 6000 by 4000 photo) or a custom width and height from 1 to 30,000 pixels each. When the clipboard holds an image, the dialog opens on a Clipboard preset of that size, listed first, so what you paste next fills the canvas exactly; pick another preset to use that instead. The background can be transparent, white or the current background color. Create makes the document and opens it in a new tab.

You can also open an image (File > Open) and it becomes a document with one layer named after the file, or drop image files onto the window.

## Tabs

Each document has a tab above the canvas. A dot on the tab marks unsaved changes. Close a tab with its button, with a middle click or with File > Close Project (Ctrl+W); Composa asks whether to save changes first. The "+" at the end of the tabs makes a new canvas, and the buttons on the right fit the canvas to the window, show it at 100 percent, and zoom in and out.

Right-click a tab for a menu that acts on that document without switching to it: Copy Image copies its whole flattened picture, whatever is selected in it; Duplicate opens a copy in a new tab; Show in Folder opens the file manager with a saved file selected; Close and Close Others close it or every other tab, asking about unsaved changes as usual.

Tool settings, the current colors and the view options carry over from one tab to the next.

## Undo

Every change is an undoable step: Edit > Undo (Ctrl+Z) and Edit > Redo (Ctrl+Shift+Z or Ctrl+Y). The menu names the step it will undo. Composa keeps up to a hundred steps.

The History panel under the Layers panel lists every step, oldest first, named as the Edit menu names it, under a first row that says how the document began. Click a step to go back or forward to it in one move, however many steps away it is, or press on the list and drag up and down to scrub through the steps while the canvas follows. The steps after the current one are dimmed and in italics: Redo brings them back, and the next change drops them. The step that is in the saved file carries a small disk, and going back to it clears the tab's unsaved-changes dot, so closing the document then asks nothing. Once more than a hundred steps have been made the oldest go, and the panel says so above the list.

Click the panel's header to collapse it to its title, drag the line above it to make it taller or shorter, and use Window > History to hide or show it. Composa remembers how you left it.

## Recovery

Every two minutes, each document with unsaved changes is written to a recovery copy. If Composa does not close normally, the next launch offers those copies in a "Recover Unsaved Work" dialog. Recover opens them, marked as modified and with "(recovered)" after their names, so you can save them where you want. Cancel discards the copies. A recovery copy is removed as soon as you save or deliberately close the document.

## Getting help

Help > Keyboard Shortcuts (F1) lists every shortcut and lets you change them. Help > About Composa shows the version you are running.
