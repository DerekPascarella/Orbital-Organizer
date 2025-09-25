# Rhea/Phoebe Sorter (The Orbital Organizer)
SD card sorter for the Sega Saturn ODEs Rhea and Phoebe.

This utility is designed to streamline the process of adding/removing games from an SD card compared to the traditional RMENU rebuild process.

It can be used for both first-time SD card set-up, as well as for users with a pre-existing Rhea/Phoebe SD card.

Please note that whether one is setting up a first-time SD card or managing a pre-existing one, the `01` folder on the card must contain all of the necessary [RMENU](https://gdemu.wordpress.com/links/) or [RmenuKai](https://ppcenter.webou.net/pskai/readme_rmenukai.txt) files and folders.

With only this `01` folder present on a brand-new SD card, the card's structure should appear as follows (with the drive letter `F:` as an example).

```
F:\
|   Rhea.ini (or Phoebe.ini)
|
\---01
    |   RMENU.exe
    |   RMENU.iso
    |
    \---BIN
        |   mkisofs.exe
        |
        \---RMENU
                0.BIN
                IP.BIN
                LIST.INI
                Z.BIN
```

## Table of Contents

- [Current Version](#current-version)
- [Changelog](#changelog)
- [Supported Operating Systems](#supported-operating-systems)
- [Known Issues](#known-issues)
- [Converting Disc Images to CloneCD Format](#converting-disc-images-to-clonecd-format)
  - [Convert Individual Disc Images With CUE2CCD](#convert-individual-disc-images-with-cue2ccd)
  - [Batch Convert Multiple Disc Images with CUE2CCD Script](#batch-convert-multiple-disc-images-with-cue2ccd-script)
  - [Sega Saturn Patcher](#sega-saturn-patcher)
  - [Manual Dumping with DAEMON Tools and CloneCD/DiscJuggler](#manual-dumping-with-daemon-tools-and-clonecddiscjuggler)
    - [Troubleshooting](#troubleshooting)
- [Basic Usage](#basic-usage)
  - [Adding New Games](#adding-new-games)
  - [Removing Existing Games](#removing-existing-games)
  - [Changing Game Labels, Virtual Folder Paths, and Other Metadata](#changing-game-labels-virtual-folder-paths-and-other-metadata)
    - [Method 1](#method-1)
    - [Method 2](#method-2)
  - [Modifying Product ID](#modifying-product-id)
  - [Multi-Disc Games with RmenuKai](#multi-disc-games-with-rmenukai)
  - [Adding an Instance of Legacy RMENU Alongside RmenuKai](#adding-an-instance-of-legacy-rmenu-alongside-rmenukai)
- [Menu Migration for Pre-Existing Rhea/Phoebe SD Cards](#menu-migration-for-pre-existing-rheaphoebe-sd-cards)

## Current Version
Rhea/Phoebe Sorter is currently at version [1.8](https://github.com/DerekPascarella/Rhea-Phoebe-Sorter/releases/download/1.8/Rhea-Phoebe.Sorter.v1.8.zip).

## Changelog
- **Version 1.8 (2025-06-05)**
  * Game labels, virtual folder paths, and disc numbers can now be modified in `GameList.txt` before processing SD card instead of solely by modifying metadata text files (e.g., `Name.txt`, `Folder.txt`) inside of numbered folders (read more [here](#method-2)).
- **Version 1.7 (2025-05-12)**
  * If files/folders are locked by another process when Rhea/Phebe Sorter attempts to move/rename them, a prompt will now be displayed giving the user the opportunity to close said processes before proceeding, instead of those locked files/folders being skipped.
  * Reduced total SD card processing time by up to 75% with new sorting algorithm.
- **Version 1.6 (2025-05-10)**
  * Fixed bug that prevented header metadata extraction on disc images of `ISO` type (thanks to [privateye](https://segaxtreme.net/members/privateye.20804/#about) for testing).
  * Enhanced header metadata extraction methods for greater reliability and accuracy.
  * For both migration and adding new games from/to RmenuKai, support added for disc images with duplicate labels, except when said duplicates are multi-disc games residing in nested virtual folder paths (i.e., an edge-case that will never occur in the real world).
- **Version 1.5 (2025-05-08)**
  * Fixed bug during migration process that ignored disc image folders after `99` (thanks to [privateye](https://segaxtreme.net/members/privateye.20804/#about) for testing).
- **Version 1.4 (2025-05-07)**
  * Added support for modifying game Product IDs.
  * Added support for a secondary instance of legacy RMENU to live alongside, and be accessible from, RmenuKai.
  * Added RmenuKai virtual folder path support during migration process.
  * Fixed issue preventing original game labels from being preserved during migration process if they contained characters that are restricted in file/folder names (thanks to [privateye](https://segaxtreme.net/members/privateye.20804/#about) for testing).
  * Added warnings and confirmation prompts to ensure users do not accidentally have files or folders on their SD card open in File Explorer or any other program during processing, as this will result in data corruption.
- **Version 1.3 (2025-05-03)**
  * Added support for automatic virtual subfolder processing of multi-disc games with RmenuKai.
- **Version 1.2 (2025-05-02)**
  * Added support for virtual folders with RmenuKai.
- **Version 1.1 (2025-02-28)**
  * Cleaned up status message output to be more compact and descriptive.
- **Version 1.0 (2024-10-18)**
    - Initial release.

## Supported Operating Systems

While Rhea/Phoebe Sorter is written in the cross-platform [Perl](https://www.perl.org) programming language, it is currently only compatible with Windows. This limitation is due to a few components that still rely on parts of the legacy RMENU rebuild process, which was also Windows-specific. Future updates aim to make the tool fully cross-platform and OS-agnostic.

## Known Issues

As of the latest version of Rhea/Phoebe Sorter, multiple game entries sharing the same labels are not supported if they are multi-disc games that are nested into one or more virtual subfolders (e.g., `/Games/RPGs/Grandia (T-En)/Disc 1` cannot co-exist with `/Games/A-Z/F-L/Grandia (T-En)/Disc 1`). However, multiple game entries sharing the same labels are supported for single-disc games that are nested into one or more virtual subfolders (e.g., `/Games/Shooters/Radiant Silvergun` can co-exist with `/Games/A-Z/M-S/Radiant Silvergun`).

Additionally, multiple game entries cannot point to the same numbered folder. This is a virtually non-existent scenario, but is sometimes seen with RmenuKai users who have a genre-separated list, as well as a simple "A-Z" listing.

## Converting Disc Images to CloneCD Format

Those new to the Rhea/Phoebe ODE may wonder the best methods for converting disc images to CloneCD format (i.e., `CCD/IMG/SUB`). There are several options, including batch conversion. In general, the below methods support conversion from any valid disc image that uses a `CUE` sheet for its table of contents (e.g., `CUE/BIN`, `CUE/ISO`, `CUE/ISO/WAV`).

### Convert Individual Disc Images With CUE2CCD
1. Drag `CUE` file onto the [CUE2CCD](https://segaxtreme.net/resources/cue2ccd.386/) program to perform conversion of a single disc image.
2. Converted disc image will be generated in a folder named `CCD`.

### Batch Convert Multiple Disc Images with CUE2CCD Script
1. Ensure that [CUE2CCD](https://segaxtreme.net/resources/cue2ccd.386/) is downloaded and in the same folder as [batch_convert_cue_to_ccd.bat](https://raw.githubusercontent.com/DerekPascarella/Rhea-Phoebe-Sorter/refs/heads/main/batch_convert_cue_to_ccd.bat).
2. Drag a folder containing disc images to be converted directly onto the batch script. Note that any degree of nested subfolders is supported.
3. Console output will appear with status update messages, and `success.log` and `error.log` files will be written to record conversion history.
4. Original disc images inside the folder dragged onto the batch script will be deleted and replaced with converted ones in `CCD/IMG/SUB` format.

### Sega Saturn Patcher
1. Launch the [Sega Saturn Patcher](https://segaxtreme.net/resources/sega-saturn-patcher.73/) application and then choose "Select Saturn Game".
2. Click "Rebuild Image" at the bottom-right of the application.
3. Under the "Save as type" dropdown, select `CCD/IMG file (*.img)`.
4. Navigate to desired output location and then click "Save" to generate converted disc image.

### Manual Dumping with DAEMON Tools and CloneCD/DiscJuggler
1. Mount the source disc image's `CUE` sheet with [DAEMON Tools](https://www.daemon-tools.cc).
2. Dump the contents of the virtual CD-ROM drive to a compatible disc image format.
   - Use [CloneCD](https://clonecd.en.softonic.com/) to dump to `CCD/IMG/SUB` format.
   - Use [DiscJuggler](https://en.wikipedia.org/wiki/DiscJuggler) to dump to `CDI` format.

#### Troubleshooting
Newer versions of Windows may cause issues with the DAEMON Tools and CloneCD/DiscJuggler method. If conversion with all options listed above fails to produce a working disc image, the following process can be followed to build a working `CCD/IMG/SUB` (or `CDI`) disc image.
1. Provision a Windows XP virtual machine using a platform like [VMware](https://www.vmware.com/products/desktop-hypervisor/workstation-and-fusion) or [VirtualBox](https://www.virtualbox.org/wiki/Downloads).
3. Inside the VM, use a specific version of DAEMON Tools ([v3.47](https://archive.org/details/daemon-tools-347)) to mount the source image.
4. Use CloneCD to dump to `CCD/IMG/SUB`, or a specific version of DiscJuggler ([v6.00.1400](https://archive.org/details/disc-juggler-pro-v-6.00.1400)) to dump to `CDI`.

## Basic Usage

![#f03c15](https://i.imgur.com/XsUAGA0.png) **IMPORTANT:** *Rhea/Phoebe users with a pre-existing SD card must first undergo a migration process described in the [Menu Migration for Pre-Existing Rhea/Phoebe SD Cards](#menu-migration-for-pre-existing-rheaphoebe-sd-cards) section.*

Using Rhea/Phoebe Sorter is simple, where each operation is carried out according to the instructions below.

### Adding New Games

1. Create a folder on the root of the SD card, giving it whatever name should appear in the RMENU/RmenuKai game list.
   - Should a label be desired that contains characters that are restricted in file/folder names (i.e., `<`, `>`, `:`, `"`, `/`, `\`, `|`, `?`, and `*`), create a file named `Name.txt` inside of the game disc folder containing said label. In this case, the name of the folder itself is ignored and not important.
   - For multi-disc games, append ` - Disc X` to the end of the folder name (or to the end of the label stored in `Name.txt`). Note that the base name must be identical across all discs. This format must be adhered to for proper processing of multi-disc games. See examples below.
     - `Policenauts (T-En) - Disc 1`
     - `Policenauts (T-En) - Disc 2`
     - `Policenauts (T-En) - Disc 3`
     - `Enemy Zero - Disc 1`
     - `Enemy Zero - Disc 2`
     - `Enemy Zero - Disc 3`
     - `Enemy Zero - Disc 4`
   - If wishing to present the disc image inside of a virtual folder path with RmenuKai, create a file named `Folder.txt` inside of the game folder, storing within it the full path (e.g., `Games/Action/Platformers`).
3. Copy the disc image (in a [supported format](https://gdemu.wordpress.com/details/rhea-details/)) to the newly created game folder.
4. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.
   - Rhea/Phoebe Sorter will alphanumerically sort all numbered folders based on game name (and virtual folder path if using RmenuKai), as well as automatically extract metadata (i.e., disc number, release date, version, and region) from each disc image so that RMENU/RmenuKai can display it. It's worth mentioning that Rhea/Phoebe Sorter's method for extracting said metadata is more reliable and accurate than that of the traditional REMENU rebuild process.

### Removing Existing Games

1. Open `GameList.txt` in the root of the SD card and then identify the numbered folder containing the disc image to be removed.
2. Remove the identified numbered folder from the SD card.
3. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.

### Changing Game Labels, Virtual Folder Paths, and Other Metadata

There are two methods by which users can modify metadata associated with an individual disc image. The first method offers total control over all pieces of metadata but can be more cumbersome, especially for bulk changes. The second method is convenient and allows quick changes, especially in bulk, but is limited to modification of game label, virtual folder path, and disc number.

#### Method 1

1. Open `GameList.txt` in the root of the SD card and then identify the numbered folder containing the disc image with metadata to be edited.
2. Open the identified numbered folder, then open and make changes to the appropriate text file.
   - `Date.txt` - The game's release date
   - `Disc.txt` - The game's disc number
   - `Folder.txt` - Optional virtual folder path for RmenuKai
   - `Name.txt` - The game name as it appears in the menu list
   - `ProductID.txt` - Optional new Product ID (see the [Modifying Product ID](https://github.com/DerekPascarella/Rhea-Phoebe-Sorter#modifying-product-id) section)
   - `Region.txt` - The game's region code
   - `Version.txt` - The game's version as specified by publisher
3. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.

#### Method 2

This method requires that an SD card is processed at least once (even without changes) by version 1.8 or newer.

1. Open `GameList.txt` in the root of the SD card and identify each disc image with metadata that is to be modified.
2. Edit `GameList.txt` directly to make desired changes to any of the three supported properties: game labels, virtual folder paths, and disc numbers.
3. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.

Note that there is minimal error handling for user mistakes when manually editing `GameList.txt`. One must be careful when making changes to avoid breaking the expected formatting.

As an example, `GameList.txt` may contain the following.

```
01 - RMENU
02 - Grandia (T-En) (Disc 1/2)
03 - Grandia (T-En) (Disc 2/2)
```

However, the user wishes to place Grandia into a virtual subfolder named `RPGs`. To do so, they would make these simple modifications.

```
01 - RMENU
02 - /RPGs/Grandia (T-En) (Disc 1/2)
03 - /RPGs/Grandia (T-En) (Disc 2/2)
```

### Modifying Product ID

The Product ID is a piece of metadata associated with Sega Saturn games which ties a piece of software to a unique identifier. There are cases where users may wish to populate a missing Product ID for homebrew software, or fix incorrect Product IDs like that of the Japanese version of "Virtua Fighter Kids" where `GS-9079` is stored on the disc but the correct ID is `GS-9098`.

If desired, Rhea/Phoebe Sorter allows users to modify this ID by directly patching the disc image's `IP.BIN` header.

1. Open `GameList.txt` in the root of the SD card and then identify the numbered folder containing the disc image for which the Product ID should be modified.
2. Open the identified numbered folder, then create a file inside of it named `ProductID.txt` containing a new ten-character ID. Fewer than ten characters is acceptable, but any ID exceeding ten characters will be trimmed.
3. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.

Note that after adding a `ProductID.txt` file to a game folder and processing the SD card, it will be automatically removed so that its disc image won't be unnecessarily patched again during future SD card processing.

### Multi-Disc Games with RmenuKai

If Rhea/Phoebe Sorter detects RmenuKai on the SD card, the user will be asked if they'd like to use virtual subfolders when processing multi-disc games. If the user says "yes", multi-disc games will only consume one entry on the RmenuKai game list. Upon selecting one of these multi-disc games, a subfolder will be displayed where each separate disc appears as a selectable entry.

For organizational purposes, it's highly recommended that those using RmenuKai allow Rhea/Phoebe Sorter to undergo this intelligent processing of multi-disc games.

### Adding an Instance of Legacy RMENU Alongside RmenuKai

Users of RmenuKai may wish to preserve an instance of legacy RMENU for certain niche use-cases (e.g., use cheats for a JHL loader game).

While users of a [Gamer's Cartridge](https://ppcenter.webou.net/satcart/) can achieve this by leaving legacy RMENU on their SD card while RmenuKai resides only on the cartridge, most Gamer's Cartridge users prefer to keep RmenuKai on their SD card in addition to their cartridge. This is useful for several reasons beyond the scope of this documentation.

Whether using RmenuKai strictly via SD card, or using it via SD card coupled with a Gamer's Cartridge, Rhea/Phoebe Sorter supports an instance of legacy RMENU that can live alongside RmenuKai and be launched directly from the game list menu.

1. Create a folder on the root of the SD card, giving it whatever name should appear in the RmenuKai game list (e.g., `RMENU`).
3. Drag the SD card onto `orbital_organizer.exe` and watch the status messages until processing is complete.
   - A message will appear informing the user that a secondary instance of RMENU was detected on their SD card, at which time they'll be asked if they'd like to update said instance with the latest game list data. Choosing to do so will result in that instance of legacy RMENU containing an up-to-date list of disc images for selection.

Note that this secondary instance of RMENU (in this case, legacy RMENU) will not reside in folder `01`. Instead, it will occupy a different folder number based on its place in the disc image list.

It is suggested to leverage RmenuKai's virtual folder path support to store legacy RMENU in a folder named `Utilities and Applications` or similar. To achieve this, before processing the SD card create a file named `Folder.txt` inside of the legacy RMENU folder containing the desired virtual folder path.

## Menu Migration for Pre-Existing Rhea/Phoebe SD Cards
For those with a pre-existing SD card who wish to use Rhea/Phoebe Sorter moving forward, a one-time migration process must be carried out. Note that this migration process fully honors virtual folder paths as defined in an RmenuKai-formatted SD card.

Before undergoing this migration, consider the following example Rhea/Phoebe SD card.

```
F:\
|   Rhea.ini
|
+---01
|   |   RMENU.exe
|   |   RMENU.iso
|   |
|   \---BIN
|       |   mkisofs.exe
|       |
|       \---RMENU
|               0.BIN
|               IP.BIN
|               LIST.INI
|               Z.BIN
|
+---02
|       Bootleg Sampler (USA).ccd
|       Bootleg Sampler (USA).img
|       Bootleg Sampler (USA).sub
|
+---03
|       game.ccd
|       game.img
|       game.sub
|
+---04
|       IMAGE.ccd
|       IMAGE.img
|       IMAGE.sub
|
+---05
|       DAYTONA USA C.C.E. NET LINK EDITION.ccd
|       DAYTONA USA C.C.E. NET LINK EDITION.img
|       DAYTONA USA C.C.E. NET LINK EDITION.sub
|
+---06
|       Advanced V.G. (Japan).ccd
|       Advanced V.G. (Japan).img
|       Advanced V.G. (Japan).sub
|
+---07
|       Grandia (English v1.1.1) (Disc 1).ccd
|       Grandia (English v1.1.1) (Disc 1).img
|       Grandia (English v1.1.1) (Disc 1).sub
|
\---08
        Grandia (English v1.1.1) (Disc 2).ccd
        Grandia (English v1.1.1) (Disc 2).img
        Grandia (English v1.1.1) (Disc 2).sub
```

To perform migration, drag the SD card onto `orbital_shift.exe`. It will produce output similar to the below.

```

Menu Migrator for Rhea/Phoebe Sorter v1.5
"The Orbital Shift"
Written by Derek Pascarella (ateam)

WARNING: Before proceeding, ensure that no files or folders on SD card (F:)
         are open in File Explorer or any other program. Failure to do so
         will result in data corruption!

Press Enter to continue...

Reading existing menu data...

RmenuKai has been detected on SD card. Virtual folder paths will be honored
during migration process.

7 disc image folder(s) found on SD card.

Renaming numbered folders...

-> Renamed folder "02" to "Bootleg Sampler (Version 1)"
   Original virtual folder path preserved in "Folder.txt" file (-Demo Discs-)
-> Renamed folder "03" to "Blue Skies (PRGE 2022 - Game Pad Version)"
   Original title preserved in "Name.txt" file (Blue Skies (PRGE 2022: Game Pad Version))
   Original virtual folder path preserved in "Folder.txt" file (-Homebrew-)
-> Renamed folder "04" to "Alphaville - The Breathtaking Blue"
   Original title preserved in "Name.txt" file (Alphaville: The Breathtaking Blue)
   Original virtual folder path preserved in "Folder.txt" file (-Media-/-CD+G-)
-> Renamed folder "05" to "Daytona USA CCE"
   Original virtual folder path preserved in "Folder.txt" file (-NetLink-)
-> Renamed folder "06" to "Advanced V.G. (JP)"
-> Renamed folder "07" to "Grandia (T-En) - Disc 1"
-> Renamed folder "08" to "Grandia (T-En) - Disc 2"

First phase of menu migration is complete!

Next, drag SD card onto "orbital_organizer.exe" to rebuild RMENU.

Press Enter to exit...

```

Afterwards, the structure of the SD card will change. Notice how games are now stored in folders named after the game itself, and `Name.txt` files are present for disc images whose labels contained characters that are restricted in file/folder names.

Additionally, because this example SD card was formatted for RmenuKai, `Folder.txt` files are present for games that had virtual folder paths associated with them.

This structure, seen below, is how new games will be added to the SD card in the future before being processed by Rhea/Phoebe Sorter.

```
F:\
|   Rhea.ini
|
+---01
|   |   RMENU.exe
|   |   RMENU.iso
|   |
|   \---BIN
|       |   mkisofs.exe
|       |
|       \---RMENU
|               0.BIN
|               IP.BIN
|               LIST.INI
|               Z.BIN
|
+---Advanced V.G. (JP)
|       Advanced V.G. (Japan).ccd
|       Advanced V.G. (Japan).img
|       Advanced V.G. (Japan).sub
|
+---Alphaville - The Breathtaking Blue
|       IMAGE.ccd
|       IMAGE.img
|       IMAGE.sub
|       Folder.txt
|       Name.txt
|
+---Blue Skies (PRGE 2022 - Game Pad Version)
|       game.ccd
|       game.img
|       game.sub
|       Folder.txt
|       Name.txt
|
+---Bootleg Sampler (Version 1)
|       Bootleg Sampler (USA).ccd
|       Bootleg Sampler (USA).img
|       Bootleg Sampler (USA).sub
|       Folder.txt
|
+---Daytona USA CCE
|       DAYTONA USA C.C.E. NET LINK EDITION.ccd
|       DAYTONA USA C.C.E. NET LINK EDITION.img
|       DAYTONA USA C.C.E. NET LINK EDITION.sub
|       Folder.txt
|
+---Grandia (T-En) - Disc 1
|       Grandia (English v1.1.1) (Disc 1).ccd
|       Grandia (English v1.1.1) (Disc 1).img
|       Grandia (English v1.1.1) (Disc 1).sub
|
\---Grandia (T-En) - Disc 2
        Grandia (English v1.1.1) (Disc 2).ccd
        Grandia (English v1.1.1) (Disc 2).img
        Grandia (English v1.1.1) (Disc 2).sub
```

It's important to note that at this point, users will want to correct folder names (or `Name.txt` files inside of them) where disc numbers are redundant. This occurs if one's original Rhea/Phoebe SD card included disc numbers in the game labels, even though RMENU automatically displays disc numbers, thus negating the need for them to be present in the game labels themselves.

For example, one might see folders (or `Name.txt` files inside of them) like this:
* `SAKURA WARS EN DISC1 - Disc 1`
* `POLICENAUTS EN DISC3 - Disc 3`

In such cases, the auto-tagged disc numbers at the end should be preserved, while the extraneous disc number labels should be removed. So, the above would be renamed as follows.
* `SAKURA WARS EN - Disc 1`
* `POLICENAUTS EN - Disc 3`

Next, drag the SD card onto `orbital_organizer.exe`, which will produce output similar to the below.

```

Rhea/Phoebe Sorter v1.5
"The Orbital Organizer"
Written by Derek Pascarella (ateam)

WARNING: Before proceeding, ensure that no files or folders on SD card (F:)
         are open in File Explorer or any other program. Failure to do so
         will result in data corruption!

Press Enter to continue...

RmenuKai has been detected on SD card. Process multi-disc games using
subfolders (i.e., "Disc 1")? (Y/N) y

Processing SD card (F:), this will take a few moments...

WARNING: Do not close this program or remove SD card! Doing so will result in
         data corruption. Please be patient.

7 disc image(s) found on SD card.

  -> Folder 02 (new: /-Demo Discs-/Bootleg Sampler (Version 1))
  -> Folder 03 (new: /-Homebrew-/Blue Skies (PRGE 2022: Game Pad Version))
  -> Folder 04 (new: /-Media-/-CD+G-/Alphaville: The Breathtaking Blue)
  -> Folder 05 (new: /-NetLink-/Daytona USA CCE)
  -> Folder 06 (new: Advanced V.G. (JP))
  -> Folder 07 (new: Grandia (T-En) - Disc 1)
  -> Folder 08 (new: Grandia (T-En) - Disc 2)

7 disc image(s) processed!

Rebuidling RMENU...

RMENU rebuild complete!

A list of disc images can be found in the "GameList.txt" file in the root of
the SD card.

Press Enter to exit...

```

After this process completes, the SD card structure changes yet again, this time to the unique format used by Rhea/Phoebe Sorter. The standard numbered folders are present, however metadata (e.g., disc number, region, virtual folder path) is now explicitly stored directly within each game folder.

```
F:\
|   Rhea.ini
|   GameList.txt
|
+---01
|   |   RMENU.exe
|   |   RMENU.iso
|   |
|   \---BIN
|       |   mkisofs.exe
|       |
|       \---RMENU
|               0.BIN
|               IP.BIN
|               LIST.INI
|               Z.BIN
|
+---02
|       Bootleg Sampler (USA).ccd
|       Bootleg Sampler (USA).img
|       Bootleg Sampler (USA).sub
|       Folder.txt
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
+---03
|       game.ccd
|       game.img
|       game.sub
|       Folder.txt
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
+---04
|       IMAGE.ccd
|       IMAGE.img
|       IMAGE.sub
|       Folder.txt
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
+---05
|       DAYTONA USA C.C.E. NET LINK EDITION.ccd
|       DAYTONA USA C.C.E. NET LINK EDITION.img
|       DAYTONA USA C.C.E. NET LINK EDITION.sub
|       Folder.txt
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
+---06
|       Advanced V.G. (Japan).ccd
|       Advanced V.G. (Japan).img
|       Advanced V.G. (Japan).sub
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
+---07
|       Grandia (English v1.1.1) (Disc 1).ccd
|       Grandia (English v1.1.1) (Disc 1).img
|       Grandia (English v1.1.1) (Disc 1).sub
|       Name.txt
|       Disc.txt
|       Region.txt
|       Version.txt
|       Date.txt
|
\---08
        Grandia (English v1.1.1) (Disc 2).ccd
        Grandia (English v1.1.1) (Disc 2).img
        Grandia (English v1.1.1) (Disc 2).sub
        Name.txt
        Disc.txt
        Region.txt
        Version.txt
        Date.txt
```

Examining the contents of the `GameList.txt` file generated in the root of the SD card reveals the following, where disc images labels, along with their virtual folder paths, are easily identified.

```
01 - RMENU
02 - /-Demo Discs-/Bootleg Sampler (Version 1)
03 - /-Homebrew-/Blue Skies (PRGE 2022: Game Pad Version)
04 - /-Media-/-CD+G-/Alphaville: The Breathtaking Blue
05 - /-NetLink-/Daytona USA CCE
06 - Advanced V.G. (JP)
07 - Grandia (T-En) (Disc 1/2)
08 - Grandia (T-En) (Disc 2/2)
```

Additionally, one can see the contents of `\01\BIN\RMENU\LIST.INI` to uncover precisely what metadata was used to generate a new RMENU/RmenuKai ISO.

```
01.title=RMENU
01.disc=1/1
01.region=JTUE
01.version=V0.2.0
01.date=20170228
02.title=/-Demo Discs-/Bootleg Sampler (Version 1)
02.disc=1/1
02.region=UT
02.date=19951026
02.version=V1.006
03.title=/-Homebrew-/Blue Skies (PRGE 2022: Game Pad Version)
03.disc=1/1
03.region=JTUE
03.date=20150923
03.version=V1.000
04.title=/-Media-/-CD+G-/Alphaville: The Breathtaking Blue
04.disc=1/1
04.region=NA
04.date=NA
04.version=NA
05.title=/-NetLink-/Daytona USA CCE
05.disc=1/1
05.region=JTUBKAEL
05.date=19970522
05.version=V1.000
06.title=Advanced V.G. (JP)
06.disc=1/1
06.region=J
06.date=19970113
06.version=V1.001
07.title=/Grandia (T-En)/Disc 1
07.disc=1/2
07.region=U
07.date=19971108
07.version=V1.002
08.title=/Grandia (T-En)/Disc 2
08.disc=2/2
08.region=U
08.date=19971117
08.version=V1.004
```

At this stage, the SD card is now ready for use. To add new games or remove existing games, simply follow the steps in the [Basic Usage](#basic-usage) section.
