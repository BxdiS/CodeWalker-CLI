<div align="center">
    <h1>CodeWalker by dexyfex</h1>
    This program is for viewing the contents of GTAV RPF archives.
</div>

## Requirements:
- PC version of GTA:V;
- 4GB RAM (8+ recommended);
- Windows 7 and above, x64 processor;
- .NET framework 4.5 or newer from [Microsoft](https://www.microsoft.com/net/download/thank-you/net471);
- DirectX 11 and Shader Model 4.0 capable graphics.

## CLI Usage (CodeWalker.CLI)
- Build/run: `dotnet run --project CodeWalker.CLI/CodeWalker.CLI.csproj -- info`
- Output formats: `--format json|xml|text` (default: `json`), legacy alias: `--xml`, compact mode: `--compact`.
- Global option: `--gtafolder <path>` (required for archive/game-file commands).
- Supported commands:
  - `info`
  - `version`
  - `rpf list`
  - `rpf inspect <stats|files|child|defrag-size> --archive <archive.rpf> ...`
  - `rpf extract [file] --archive <archive.rpf> --entry <entryPath> --output <path>`
  - `rpf extract scripts --archive <archive.rpf> --output <path>`
  - `rpf extract test-all --archive <archive.rpf>`
  - `rpf create root-archive --relpath <path.rpf> [--encryption OPEN|NONE|AES|NG] --force true`
  - `rpf create archive --archive <archive.rpf> --dir <dirPath> --name <name.rpf> [--encryption OPEN|NONE|AES|NG] --force true`
  - `rpf create directory --archive <archive.rpf> --dir <dirPath> --name <dirName> --force true`
  - `rpf create file --archive <archive.rpf> --dir <dirPath> --name <fileName> --input <localFile> [--overwrite true|false] --force true`
  - `rpf edit rename-entry --path <entryPath> --newname <name> --force true`
  - `rpf edit delete-entry --path <entryPath> --force true`
  - `rpf crypto is-valid --archive <archive.rpf> [--recursive true|false]`
  - `rpf crypto ensure-valid --archive <archive.rpf> [--recursive true|false] --force true`
  - `rpf crypto set --archive <archive.rpf> --encryption OPEN|NONE|AES|NG --force true`
  - `rpf maintenance defrag-size --archive <archive.rpf> [--recursive true|false]` (alias: `defragment-size`)
  - `rpf maintenance defragment --archive <archive.rpf> [--recursive true|false] --force true`
  - `rpf util compress --input <path> --output <path>`
  - `rpf util decompress --archive <archive.rpf> --input <path> --output <path>`
  - `rpf util flags-from-size --size <n> [--version <n>]`
  - `rpf util flags-from-blocks --block-count <n> --block-size <n> [--version <n>]`
  - `rpf util size-from-flags --flags <uint|0xhex>`
  - `rpf util version-from-flags --sys-flags <uint|0xhex> --gfx-flags <uint|0xhex>`
  - `rpf util page-flags --flags <uint|0xhex>`
  - `file info --path <localFileOrRpfEntryPath>`
  - `file export --path <rpfEntryPath> --output <path>`
  - `search --pattern <text> [--limit <n>]`

### `CodeWalker.Core/GameFiles/RpfFile.cs` coverage map

| RpfFile method | CLI command | Status | Reason / Notes | Example |
|---|---|---|---|---|
| `RpfFile(..)` constructors | internal | not-exposed | Internal lifecycle objects; used by manager/scanner | n/a |
| `CopyToModsFolder` | n/a | planned | Mutating FS behavior, not yet surfaced | n/a |
| `IsInModsFolder` | `rpf inspect stats` | implemented | Returned in archive metadata | `rpf inspect stats --archive x64a.rpf` |
| `GetTopParent` | `rpf inspect stats` | implemented | Returned in archive metadata | same as above |
| `GetPhysicalFilePath` | `rpf inspect stats` | implemented | Returned in archive metadata | same as above |
| `ScanStructure` | `rpf list`/all archive commands | implemented | Called through `RpfManager.Init` | `rpf list` |
| `ExtractScripts` | `rpf extract scripts` | implemented | Recursive `.ysc` extraction | `rpf extract scripts --archive x.rpf --output out` |
| `ExtractFile` | `rpf extract`/`file info`/`file export` | implemented | Primary file extraction API | `rpf extract --archive x.rpf --entry a\\b.ydr --output out` |
| `ExtractFileBinary` | `rpf extract` (via `ExtractFile`) | implemented | Internal branch selected by entry type | same as above |
| `ExtractFileResource` | `rpf extract` (via `ExtractFile`) | implemented | Internal branch selected by entry type | same as above |
| `GetFile<T>(RpfEntry)` | internal | not-exposed | Generic parser helper; used inside `file info` metadata branch | n/a |
| `GetFile<T>(RpfEntry, byte[])` | `file info` | implemented | Used for typed metadata probing | `file info --path <rpfEntryPath>` |
| `GetResourceFile<T>` | n/a | not-exposed | Generic typed API unsuitable for raw CLI surface | n/a |
| `LoadResourceFile<T>` | n/a | not-exposed | Generic mutating load helper | n/a |
| `CreateResourceFileEntry` | n/a | not-exposed | Low-level helper used by generic load methods | n/a |
| `TestExtractAllFiles` | `rpf extract test-all` | implemented | Diagnostics extraction report | `rpf extract test-all --archive x.rpf` |
| `GetFiles(string,bool)` | `rpf inspect files` | implemented | Folder listing API | `rpf inspect files --archive x.rpf --folder x64\\... --recurse true` |
| `GetFiles(RpfDirectoryEntry,...)` | `rpf inspect files` | implemented | Recursive worker used by public overload | same as above |
| `DecompressBytes` | `rpf util decompress` | implemented | Requires archive context instance | `rpf util decompress --archive x.rpf --input in --output out` |
| `CompressBytes` | `rpf util compress` | implemented | Static compressor exposed directly | `rpf util compress --input in --output out` |
| `FindChildArchive` | `rpf inspect child` | implemented | Maps binary entry to child archive | `rpf inspect child --archive x.rpf --entry x\\y.rpf` |
| `GetDefragmentedFileSize` | `rpf inspect defrag-size` | implemented | Read-only sizing estimate | `rpf inspect defrag-size --archive x.rpf --recursive true` |
| `CreateNew(gtafolder, relpath, ..)` | `rpf create root-archive` | implemented | Creates new file system archive (mutating, requires `--force true`); `--relpath` accepts path relative to `--gtafolder` or an absolute path | `rpf create root-archive --relpath mods\\update\\x.rpf --force true` |
| `CreateNew(dir, name, ..)` | `rpf create archive` | implemented | Creates child archive in existing RPF (mutating, requires `--force true`) | `rpf create archive --archive x.rpf --dir x64\\... --name y.rpf --force true` |
| `CreateDirectory` | `rpf create directory` | implemented | Creates directory entry (mutating, requires `--force true`) | `rpf create directory --archive x.rpf --dir x64\\... --name newdir --force true` |
| `CreateFile` | `rpf create file` | implemented | Imports local file into RPF (mutating, requires `--force true`) | `rpf create file --archive x.rpf --dir x64\\... --name a.bin --input ./a.bin --force true` |
| `RenameArchive` | n/a | not-exposed | Runtime path relink only; no direct disk rename semantics | n/a |
| `RenameEntry` | `rpf edit rename-entry` | implemented | Renames archive entry (mutating, requires `--force true`) | `rpf edit rename-entry --path x64\\...\\a.ytd --newname b.ytd --force true` |
| `DeleteEntry` | `rpf edit delete-entry` | implemented | Deletes entry recursively for directories (mutating, requires `--force true`) | `rpf edit delete-entry --path x64\\...\\old.ydr --force true` |
| `IsValidEncryption` | `rpf crypto is-valid` | implemented | Read-only encryption validation | `rpf crypto is-valid --archive x.rpf --recursive true` |
| `EnsureValidEncryption` | `rpf crypto ensure-valid` | implemented | Converts archive chain to valid OPEN encryption (mutating, requires `--force true`) | `rpf crypto ensure-valid --archive x.rpf --recursive true --force true` |
| `SetEncryptionType` | `rpf crypto set` | implemented | Directly sets encryption type (mutating, requires `--force true`) | `rpf crypto set --archive x.rpf --encryption OPEN --force true` |
| `Defragment` | `rpf maintenance defragment` | implemented | Defragments archive layout (mutating, requires `--force true`) | `rpf maintenance defragment --archive x.rpf --recursive true --force true` |
| `ToString()` overrides | n/a | not-exposed | Diagnostic/object display only | n/a |
| `RpfEntry.Read/Write` abstract | n/a | not-exposed | Serialization internals | n/a |
| `RpfEntry.GetShortName/GetShortNameLower` | internal | not-exposed | Naming helpers consumed by core ops | n/a |
| `RpfDirectoryEntry.Read/Write/ToString` | n/a | not-exposed | Structure internals | n/a |
| `RpfFileEntry.GetFileSize/SetFileSize` abstract | n/a | not-exposed | Entry internals | n/a |
| `RpfBinaryFileEntry.Read/Write/GetFileSize/SetFileSize/ToString` | n/a | not-exposed | Entry internals | n/a |
| `RpfResourceFileEntry.GetSizeFromFlags` | `rpf util size-from-flags` | implemented | Utility exposed | `rpf util size-from-flags --flags 0x60000000` |
| `RpfResourceFileEntry.GetFlagsFromSize` | `rpf util flags-from-size` | implemented | Utility exposed | `rpf util flags-from-size --size 16384 --version 0` |
| `RpfResourceFileEntry.GetFlagsFromBlocks` | `rpf util flags-from-blocks` | implemented | Utility exposed | `rpf util flags-from-blocks --block-count 64 --block-size 512` |
| `RpfResourceFileEntry.GetVersionFromFlags` | `rpf util version-from-flags` | implemented | Utility exposed | `rpf util version-from-flags --sys-flags 0x.. --gfx-flags 0x..` |
| `RpfResourceFileEntry.Read/Write/GetFileSize/SetFileSize/ToString` | n/a | not-exposed | Entry internals | n/a |
| `RpfResourcePageFlags(..)` ctors | `rpf util page-flags` | implemented | Decoder object instantiated by CLI | `rpf util page-flags --flags 0x..` |
| `RpfResourcePageFlags implicit operators` | n/a | not-exposed | Language conversion helpers | n/a |
| `RpfResourcePageFlags.ToString` / `RpfResourcePage.ToString` | n/a | not-exposed | Debug display only | n/a |

# App Usage:
On first startup, the app will prompt to browse for the GTA:V game folder. If you have the Steam version installed
in the default location `(C:\Program Files (x86)\Steam\SteamApps\common\Grand Theft Auto V)`, then this step will be skipped automatically.

The World View will load by default. It will take a while to load. Use the WASD keys to move the camera. Hold shift to move faster. Drag the left mouse button to rotate the view. Use the mouse wheel to zoom in/out, and change the base movement speed. (Zoom in = slower motion) Xbox controller input is also supported. The Toolbox can be shown by clicking the `<<` button in the top right-hand corner of the screen. `T` opens the main toolbar.

First-person mode can be activated with the P key, or by pressing the Start button on the XBox controller. While in first-person mode, the left mouse button (or right trigger) will fire an egg.

Entities can be selected (with the right mouse button) by enabling the option on the Selection tab in the toolbox. The details of the selected entity, its archetype, and its drawable can be explored in the relevant sub-tabs. (This option can also be activated with the arrow button on the toolbar).

When an entity is selected, `E` will switch to edit mode (or alternatively, edit mode can be activated by switching the Widget mode to anything other than Default). When in edit mode, `Q` will exit edit mode, `W` toggles the position widget, E toggles rotation, and R toggles scale. Also when in edit mode, movement is still WSAD, but only while you're holding the left mouse button down, and not interacting with the widget. `Ctrl-Z` and `Ctrl-Y` will Undo and Redo entity transformation (position/rotation/scale) actions.

The Project Window allows a CodeWalker project to be created (`.cwproj`), and files added to it. Editing entities while the Project Window is open will add the entity's `.ymap` to the current project. `Ymap` files can then be saved to disk, for use in a map mod. New `ymap` files can also be created, and entities can be added and removed. Also supported for editing are `.ynd` files (traffic paths), trains `.dat files` (train tracks), and scenarios (`.ymt`). (A full tutorial on making map mods is out of the scope of this readme.)

A full explanation of all the tools in this application is still on the to-do list! The user is currently left to explore the options at their own peril. Note some options may cause CodeWalker to crash, or otherwise stop working properly. Restart the program if this happens! Also note that this program is a constant work in progress, so bugs and crashes are to be expected. Some parts of the world do not yet render correctly, but expect updates in the future to fix these issues.

# Menu Mode:
The app can also be started with a main menu instead of loading the world view. This can be useful for situations where the world
view is not needed, and the world loading can be avoided. To activate the menu mode, run CodeWalker with the 'menu' command line argument, e.g: CodeWalker.exe menu.

# Explorer Mode:
The app can be started with the `'explorer'` command line argument. This displays an interface much like OpenIV, with a Windows-Explorer style interface for browsing the game's .rpf archives. Double-click on files to open them. Viewers for most file types are available, but hex view will be shown as a fallback. To activate the explorer mode, run the command: CodeWalker.exe explorer. Alternatively, run the CodeWalker Explorer batch file in the program's directory.

# Main Toolbar:
The main toolbar is used to access most of the editing features in CodeWalker. Shortcuts for new, open and create files are provided. The selection mode can be changed with the "pointer" button. Move, rotate and scale buttons provide access to the different editing widget modes. Other shortcuts on the toolbar include buttons to open the Selection Info window, and the Project window. See the tooltips on the toolbar items for hints.

# Project Window:
The project window is the starting point for editing files in CodeWalker. Project files can be created, and files can be added to them. It is recommended to create and save a project file before adding files to be edited and saved. The tree view displays the files in the current project, and their contents.

# YMAP Editing:
New `YMAP` files can be created via the project window, and existing `YMAP` files can be edited. To edit an existing single player `YMAP`, first change codewalker DLC level to `patchday2ng`, and enable DLC. Open the toolbar, and enable Entity selection mode. Enable the Move widget with the toolbar Move button. Open the project window with the toolbar button. Changes made while the project window is open are automatically added to the project. Select an entity to edit by right clicking when the entity is moused over, and its bounding box shown in white. Move, rotate and/or scale the selected entity with the widget. When the first change is made, the entity's `YMAP` will be added to the current project. If no project is open, a new one will be created. The edited `YMAP` file can be saved to the drive using the File menu in the project window. After saving the file, it needs to be added into the mods folder. Using OpenIV, find the existing `YMAP` file using the search function (note: the correct path for the edited `YMAP` can be found in the selection info window in CodeWalker, when an entity is selected, look for `YMap`>`RpfFileEntry` in the selection info property grid). Replace the edited `YMAP` into a copy of the correct archive in the /mods folder. Newly created YMAPs can be added to DLC archives in the same manner.

# Train Tracks Editing:
[TODO - write this!]

# (YND) Traffic Paths Editing:
[TODO - write this!]

# (YMT) Scenario Regions Editing:
[TODO: write this!] <br>
See https://youtu.be/U0nrVL44Fb4 - Scenario Editing Tutorial

# Regarding game files: (FYI)

The PC GTAV world is stored in the `RPF` archives in many different file formats. As expected, some formats are used for storing rendering-related content, for example the textures and 3d models, while other formats are used for storing game and engine related data.

The main formats when it comes to rendering GTAV content are:
`.ytd` - Texture Dictionary - Stores texture data in a DirectX format convenient for loading to the GPU. 
`.ydr` - Drawable - Contains a single asset's 3d model. Can contain a Texture Dictionary, and up to 4 LODs of a model.
`.ydd` - Drawable Dictionary - A collection of Drawables packed into a single file.
`.yft` - Fragment - Contains a Drawable, along with other metadata for example physics data.

The content Assets are pieced together to create the GTAV world via MapTypes (Archetypes) and MapData (Entity placements). At a high level, Archetypes define objects that are placeable, and Entities define where those objects are placed to make up the world. The collision mesh data for the world is stored in Bounds files.

### The formats for these are:
`.ytyp` - MapTypes - Contains a group of MapTypes (Archetypes), each defining an object that could be placed.
`.ymap` - MapData - Contains placements of Archetypes, each defining an Entity in the world.
`.ybn` - Bounds - Contains collision mesh / bounding data for pieces of the world.

The EntityData contained within the MapData (`.ymap`) files forms the LOD hierarchy. This hierarchy is arranged such that the lowest detail version of the world, at the root of the hierarchy, is represented by a small number of large models that can all be rendered simultaneously to draw the world at a great distance. The next branch in the hierarchy splits each of these large models into a group of smaller objects, each represented in a higher detail than the previous level. This pattern is continued for up to 6 levels of detail. When rendering the world, the correct level of detail for each branch in the hierarchy needs to be determined, as obviously the highest detail objects cannot all be rendered at once due to limited computing resources.

In CodeWalker, This is done by recursing the LOD tree from the roots, checking how far away from the camera the node's Entity is. If it is below a certain value, then the current level is used, otherwise it moves to the next higher level, depending on the LOD distance setting. (In the Ymap view, the highest LOD, ORPHANHD, is not rendered by default. The ORPHANHD entities can often be manually rendered by specifying the correct `strm ymap` file for the area in question in the `ymap` text box. The `strm ymap` name can often be found by mouse-selecting a high detail object in the area and noting what `ymap` the entity is contained in, in the selection details panel.)
