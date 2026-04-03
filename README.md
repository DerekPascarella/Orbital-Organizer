# Orbital Organizer
An SD card management tool for the SEGA Saturn ODEs Rhea and Phoebe.

As of version 2.0.0, Orbital Organizer has been completely rewritten from a command-line tool to a cross-platform GUI application.

The GUI design and workflow are inspired by [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager) by Sonik, the equivalent SD card management tool for SEGA Dreamcast GDEMU users, which was also used for the [openMenu Virtual Folder Bundle](https://github.com/DerekPascarella/openMenu-Virtual-Folder-Bundle).

Please note that Rhea/Phoebe SD cards must be formatted as FAT32.

## Table of Contents

- [Current Version](#current-version)
- [Changelog](#changelog)
- [Credits](#credits)
- [Supported Platforms](#supported-platforms)
- [Supported Disc Image Formats](#supported-disc-image-formats)
- [Converting Disc Images to CDI Format](#converting-disc-images-to-cdi-format)
  - [Troubleshooting](#troubleshooting)
- [Basic Usage](#basic-usage)
  - [Loading an SD Card](#loading-an-sd-card)
  - [Adding Games](#adding-games)
  - [Removing Games](#removing-games)
  - [Editing Game Information](#editing-game-information)
  - [Modifying Product ID](#modifying-product-id)
  - [Reordering Games](#reordering-games)
  - [Searching and Filtering](#searching-and-filtering)
  - [Saving Changes](#saving-changes)
  - [Undo and Redo](#undo-and-redo)
  - [Disc Image Info](#disc-image-info)
- [Menu Type Options](#menu-type-options)
- [SD Card Compatibility](#sd-card-compatibility)
- [Credits](#credits)
- [Legal and Licensing](#legal-and-licensing)
  - [Orbital Organizer](#orbital-organizer-1)
  - [RMENU](#rmenu)
  - [RmenuKai](#rmenukai)
  - [Third-Party Components](#third-party-components)

## Current Version
Orbital Organizer is currently at version [2.0.0](https://github.com/DerekPascarella/Orbital-Organizer/releases/tag/2.0.0).

## Changelog
- **Version 2.0.0 (2026-04-03)**
  - Complete rewrite from console application to cross-platform GUI (Windows, macOS, Linux).
  - RMENU and RmenuKai now bundled, so users no longer need to source either themselves.
  - Disc images can now be assigned to more than one virtual folder path.
  - Compressed archives (ZIP, 7z, RAR) containing disc images can now be added directly.
  - CUE-based disc images are automatically converted to CloneCD format (CCD/IMG/SUB) when saved to the SD card.
  - CHD disc images are supported and automatically decompressed to CUE/BIN before conversion to CCD/IMG/SUB.
  - New `IP.BIN` header parsing technique implemented for improved metadata accuracy, verified against all 2,455 known Saturn disc images catalogued by [Redump](http://redump.org/discs/system/ss/).
  - Auto-update functionality added for Windows and Linux builds (macOS present noy supported).
- **Version 1.8 (2025-06-05)**
  - Game labels, virtual folder paths, and disc numbers can now be modified in `GameList.txt` before processing SD card instead of solely by modifying metadata text files (e.g., `Name.txt`, `Folder.txt`) inside of numbered folders.
- **Version 1.7 (2025-05-12)**
  - If files/folders are locked by another process when Rhea/Phoebe Sorter attempts to move/rename them, a prompt will now be displayed giving the user the opportunity to close said processes before proceeding, instead of those locked files/folders being skipped.
  - Reduced total SD card processing time by up to 75% with new sorting algorithm.
- **Version 1.6 (2025-05-10)**
  - Fixed bug that prevented header metadata extraction on disc images of ISO type.
  - Enhanced header metadata extraction methods for greater reliability and accuracy.
  - For both migration and adding new games from/to RmenuKai, support added for disc images with duplicate labels, except when said duplicates are multi-disc games residing in nested virtual folder paths.
- **Version 1.5 (2025-05-08)**
  - Fixed bug during migration process that ignored disc image folders after `99`.
- **Version 1.4 (2025-05-07)**
  - Added support for modifying game Product IDs.
  - Added support for a secondary instance of legacy RMENU to live alongside, and be accessible from, RmenuKai.
  - Added RmenuKai virtual folder path support during migration process.
  - Fixed issue preventing original game labels from being preserved during migration process if they contained characters that are restricted in file/folder names.
  - Added warnings and confirmation prompts to ensure users do not accidentally have files or folders on their SD card open in File Explorer or any other program during processing, as this will result in data corruption.
- **Version 1.3 (2025-05-03)**
  - Added support for automatic virtual subfolder processing of multi-disc games with RmenuKai.
- **Version 1.2 (2025-05-02)**
  - Added support for virtual folders with RmenuKai.
- **Version 1.1 (2025-02-28)**
  - Cleaned up status message output to be more compact and descriptive.
- **Version 1.0 (2024-10-18)**
  - Initial release.

## Credits

- **Programming**
  - Derek Pascarella (ateam)
- **Testing**
  - Multimod
  - Kanji

## Supported Platforms

| Platform | Architecture | Download | Notes |
|----------|-------------|----------|-------|
| Windows | x64 | `.zip` | Requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| Windows | x86 | `.zip` | Requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) |
| macOS | Apple Silicon | `.tar.gz` (`.app` bundle) | Self-contained, no runtime needed |
| macOS | Intel | `.tar.gz` (`.app` bundle) | Self-contained, no runtime needed |
| Linux | x64 | `.tar.gz` | Self-contained, no runtime needed |

## Supported Disc Image Formats

| Format | Extension(s) | Notes |
|--------|-------------|-------|
| ISO 9660 | `.iso` | Single-file disc image |
| DiscJuggler | `.cdi` | Single-file disc image |
| Alcohol 120% | `.mds`, `.mdf` | Two-file set |
| CloneCD | `.ccd`, `.img`, `.sub` | Three-file set |
| CUE-based | `.cue` (+ `.bin`, `.iso`, `.wav`, etc.) | Automatically converted to CCD/IMG/SUB |
| CHD | `.chd` | Decompressed to CUE/BIN then converted to CCD/IMG/SUB |
| Compressed | `.zip`, `.7z`, `.rar` | Archives containing any of the above formats |

On Windows, CUE-based disc images (including those inside compressed archives) are automatically converted to CloneCD format (CCD/IMG/SUB) when saved to the SD card using the bundled [CUE2CCD](https://segaxtreme.net/resources/cue2ccd.386/) tool. CHD disc images are first decompressed to CUE/BIN using [libchdr](https://github.com/rtissera/libchdr), then converted to CCD/IMG/SUB in the same way. CUE-based and CHD disc images are not supported on macOS or Linux and must be converted to a compatible format (CCD, CDI, or ISO) before adding them.

## Converting Disc Images to CDI Format

Some modified Sega Saturn games may require conversion to DiscJuggler (CDI) format. This can be accomplished using [DAEMON Tools](https://www.daemon-tools.cc) and [DiscJuggler](https://en.wikipedia.org/wiki/DiscJuggler).

1. Mount the source disc image's CUE sheet with [DAEMON Tools](https://www.daemon-tools.cc).
2. Use [DiscJuggler](https://en.wikipedia.org/wiki/DiscJuggler) to dump the contents of the virtual CD-ROM drive to CDI format.

### Troubleshooting
Newer versions of Windows may cause issues with the DAEMON Tools and DiscJuggler method. If conversion fails to produce a working disc image, the following process can be used.
1. Provision a Windows XP virtual machine using a platform like [VMware](https://www.vmware.com/products/desktop-hypervisor/workstation-and-fusion) or [VirtualBox](https://www.virtualbox.org/wiki/Downloads).
2. Inside the VM, use a specific version of DAEMON Tools ([v3.47](https://archive.org/details/daemon-tools-347)) to mount the source image.
3. Use a specific version of DiscJuggler ([v6.00.1400](https://archive.org/details/disc-juggler-pro-v-6.00.1400)) to dump to CDI.

## Basic Usage

### Loading an SD Card
Select the SD card drive from the **SD Drive** dropdown, or click the folder icon to browse for a custom folder. Once selected, Orbital Organizer scans numbered folders on the card and reads sidecar metadata files (`Name.txt`, `Folder.txt`, `Disc.txt`, etc.) from each game folder.

If sidecar metadata files are missing, Orbital Organizer will prompt to scan disc images for `IP.BIN` header data. This extracts the game title, product ID, region, version, and release date directly from each disc image.

The **Temp. Folder** setting controls where temporary files are stored during operations. By default, the system temp directory is used.

### Adding Games
New games can be added by clicking the **+** button or by dragging disc image files, folders, or compressed archives directly onto the game list. Added games appear with "Other" in the **Location** column until changes are saved to the SD card.

### Removing Games
Select one or more games in the list and click the **-** button. The corresponding numbered folders are deleted from the SD card when changes are saved.

### Editing Game Information
Double-click a cell in the **Title**, **Folder** (RmenuKai only), **Product ID**, or **Disc** column to edit its value inline. The **Disc** column uses X/Y format (e.g., 1/1, 2/4). When using RmenuKai, multi-disc games that share the same title are automatically grouped into a single menu entry with a disc selection prompt. Each disc in the set must have the same title and a sequential disc number (e.g., 1/4, 2/4, 3/4, 4/4).

Multiple games can be selected at once for bulk operations. Right-clicking opens a context menu with the following options, most of which support multi-select:
- **Rename** - Rename the selected game title (single selection only).
- **Title Case** / **Uppercase** / **Lowercase** - Change the case of all selected game titles.
- **Automatically Rename Title** - Rename all selected titles using one of three sources:
  - **Using `IP.BIN` info** - Extract titles from each disc image's `IP.BIN` header.
  - **Using its folder name on computer** - Use the name of each source folder on the computer (only available for newly added games).
  - **Using disc image's base file name** - Use the file name of each disc image (only available for newly added games).
- **Assign Folder Path** (RmenuKai only) - Set the virtual folder path for all selected games (e.g., `Games\JRPG`).
- **Assign Additional Folder Paths** (RmenuKai only) - Set up to five alternative virtual folder paths for a game, allowing it to appear in multiple folders (single selection only).

### Modifying Product ID
The Product ID is a piece of metadata associated with Sega Saturn games which ties a piece of software to a unique identifier. There are cases where it may be useful to populate a missing Product ID for homebrew software, or fix incorrect Product IDs like that of the Japanese version of "Virtua Fighter Kids" where `GS-9079` is stored on the disc but the correct ID is `GS-9098`.

Orbital Organizer allows modification of this ID by editing the **Product ID** column directly. When changes are saved, the disc image's `IP.BIN` header is patched with the new value.

### Reordering Games
Use the **up** and **down** arrow buttons to move a selected game in the list. The **Sort List** button sorts all games alphabetically by a combination of folder path (RmenuKai only), title, and disc number.

### Searching and Filtering
The **Search/Filter** text box accepts search terms that match against game titles and product IDs.
- The **search** button (magnifying glass) navigates to the next matching entry in the list.
- The **filter** button (funnel) hides all non-matching entries, showing only games that match the search term.
- The **reset** button clears the filter and restores the full game list.

### Saving Changes
Clicking **Save Changes** writes all pending changes to the SD card. This includes:
- Renumbering game folders sequentially (starting from `02`).
- Copying new game files to the SD card.
- Converting CUE-based disc images to CloneCD format.
- Rebuilding the RMENU/RmenuKai menu ISO in folder `01`.
- Generating `LIST.INI` for the RMENU/RmenuKai menu system.
- Generating `GameList.txt` with a formatted table of all games on the card.
- Writing or updating sidecar metadata files in each game folder.
- Removing orphaned numbered folders that no longer correspond to any game in the list.

The **File/Folder Lock Check** checkbox enables a pre-save scan that checks for files or folders locked by another process. If locked files are detected, a dialog is displayed listing them so they can be closed before proceeding.

### Undo and Redo
The **Undo** and **Redo** buttons support up to 10 levels of undo/redo for all list operations (adding, removing, reordering, editing).

### Disc Image Info
Clicking the **info** button next to a game entry opens a dialog displaying metadata parsed from the disc image's `IP.BIN` header:
- **Title** - The game title as stored in the disc image.
- **Product ID** - The product/serial number.
- **Version** - The software version.
- **Release Date** - The release date.
- **Region** - The region code (e.g., J, T, U, E).
- **Format** - The disc image format (e.g., ISO, CDI, CCD/IMG/SUB, MDS/MDF).
- **Size** - The total size of all disc image files.
- **Folders** - All virtual folder paths assigned to the disc image (RmenuKai only).

## Menu Type Options

The **Menu Type** setting controls which menu system Orbital Organizer targets when building the SD card. Three options are available:

- **RmenuKai** - Targets [RmenuKai](https://ppcenter.webou.net/pskai/) by cafe-alpha. This enables virtual folder support and alternative folder paths, allowing games to be organized into categories without physically moving files. Orbital Organizer currently ships with RmenuKai v6.545.
- **RMENU** - Targets the legacy RMENU by [CyberWarriorX](https://github.com/cyberwarriorx). Games appear in a flat list with no folder support. Orbital Organizer currently ships with RMENU v0.2.
- **Both** - Targets RmenuKai as the primary menu in folder `01`, with a secondary instance of legacy RMENU in its own numbered folder that can be launched directly from the RmenuKai game list. The secondary RMENU instance does not reside in folder `01`. Instead, it occupies a numbered folder based on its position in the game list. This is useful for niche cases such as launching JHL loader cheats via legacy RMENU, and is commonly used alongside a [Gamer's Cartridge](https://ppcenter.webou.net/satcart/), though there are several other reasons a user may want both menu systems available.

Orbital Organizer ships with a bundled version of RmenuKai. To update to a newer release, replace the `0.BIN` file in the `tools\rmenukai` folder with the updated version from the [RmenuKai website](https://ppcenter.webou.net/pskai/). The new version will be used the next time changes are saved to the SD card.

## SD Card Compatibility

Orbital Organizer works with any existing Rhea/Phoebe SD card out of the box, regardless of how it was previously set up or managed. This includes SD cards built manually, SD cards managed by the traditional RMENU/RmenuKai rebuild process, and SD cards managed by the previous version of this tool.

When loading an SD card for the first time, Orbital Organizer reads `LIST.INI` to recover game titles and optional virtual folder paths, then scans disc images for any remaining metadata. No manual migration or preparation is needed.

## Credits

- **Programming**
  - Derek Pascarella (ateam)
- **Special Thanks**
  - Sonik for his work on [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager), upon which the Orbital Organizer GUI is based

## Legal and Licensing

### Orbital Organizer
**Copyright (C) 2026, Derek Pascarella (ateam)**

Licensed under the GNU General Public License v3.0 (GPL-3.0).

Repository: https://github.com/DerekPascarella/Orbital-Organizer

The GUI design and workflow of Orbital Organizer are inspired by [GD MENU Card Manager](https://github.com/sonik-br/GDMENUCardManager) by Sonik (GPL-3.0), the equivalent SD card management tool for SEGA Dreamcast GDEMU users.

For the full license text, see `LICENSE`.

### RMENU
RMENU was originally created by [CyberWarriorX](https://github.com/cyberwarriorx). Orbital Organizer rebuilds RMENU ISO images for Rhea/Phoebe compatibility.

### RmenuKai
[RmenuKai](https://ppcenter.webou.net/pskai/) was originally created by cafe-alpha. Orbital Organizer supports RmenuKai's virtual folder system and alternative folder paths.

### Third-Party Components
- [DiscUtils](https://github.com/DiscUtils/DiscUtils) (MIT) - ISO 9660 image building
- [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT) - archive extraction
- [Avalonia UI](https://avaloniaui.net/) (MIT) - cross-platform GUI framework
- [ByteSize](https://github.com/omar/ByteSize) (MIT) - file size formatting
