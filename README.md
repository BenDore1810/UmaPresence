# UmaPresence

Discord Rich Presence for *Umamusume: Pretty Derby* (JP DMM) on Windows.

[![Release](https://img.shields.io/github/v/release/BenDore1810/UmaPresence?include_prereleases&label=release)](https://github.com/BenDore1810/UmaPresence/releases/tag/v0.1.0-beta.1)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4)
[![License](https://img.shields.io/badge/license-GPL--3.0-green)](LICENSE)

UmaPresence follows what you are doing in the game and updates your Discord activity card as you play. Show your friends which race you are in, whose Legacy you are viewing, or who is on stage in Live Theater. [Download the Windows prerelease](https://github.com/BenDore1810/UmaPresence/releases/tag/v0.1.0-beta.1).

> **Unofficial third-party app.** Rich Presence reads what is happening in the game and sends it to Discord. Setup loads a bridge in the game through Hachimi Edge or Standalone. Standalone also offers optional controls for concert playback and casting. Proceed with caution, especially after a game update.

## Contents

- [Preview](#preview)
- [Features](#features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Uninstalling](#uninstalling)
- [Troubleshooting and diagnostics](#troubleshooting-and-diagnostics)
- [Building from source](#building-from-source)
- [Verification](#verification)
- [Acknowledgements](#acknowledgements)
- [License](#license)

## Preview

### Live Theater · STARTING FORCE

![STARTING FORCE on Discord, showing Almond Eye followed by the Gran Alegria and Daring Tact hover tooltips](assets/discord-live-concert.gif)

The card shows the song, cast, and playback progress. Hover over the rotating cast portrait to see the singer's costume and voice actor.

### Legacy · Mayano Top Gun

![Mayano Top Gun's Legacy activity card on Discord](assets/discord-legacy.png)

Discord shows the Uma's portrait, rank, evaluation score, and skill count. This one is US7 with a score of 72,728 and 79 skills.

And more to come.

## Features

| Area | What Discord shows |
| --- | --- |
| Home and menus | Home, Veterans, character and support card collections, deck editing, and Scout results |
| Career | Preparing a career, training, learning skills, and reviewing results |
| Uma profiles | Legacy, Bare Legacy, Practice Partners, and Rivals, with rank, costumes, and profile details where available |
| Races | Preparation, live standings, and results for Career races, Champions Meeting, Room Matches, League of Heroes, and Master Challenge |
| Live Theater | Concert selection, song title, cast, and playback progress |
| Other | Story, Story Events, Missions, and more |

## Requirements

- Windows x64
- .NET Framework 4.8
- Discord desktop
- Umamusume: Pretty Derby (JP DMM). Steam and Global are not supported.

The game, the official engine, the Cellar helper, and Hachimi are obtained separately. The game engine is copied only from your own installation. The release ZIP includes the UmaPresence loader and bridge.

## Installation

Download the Windows app ZIP from the [public prerelease](https://github.com/BenDore1810/UmaPresence/releases/tag/v0.1.0-beta.1). This prerelease uses manual updates from GitHub Releases. See `acceptance.json` in the ZIP for the checks that are still pending.

1. Extract the entire ZIP into a new, writable folder and run `UmaPresence.exe`.
2. On first launch, choose **Set up game integration** to confirm the detected game folder, or **Skip for now** to open Overview. Use **Browse…** in Game integration if the folder is blank or you want a different one.
3. Close the game and set up your integration.
   - For **Standalone**, download or select the official Cellar helper, then click **Install Standalone**.
   - For an existing supported **Hachimi Edge** setup, choose **Hachimi** and click **Apply switch**.

   UmaPresence installs and registers the bridge for the selected mode.
4. Wait for the success message, then launch the game through DMM. If Hachimi's built-in Discord activity is enabled, turn it off first so UmaPresence can publish activity.
5. Enable Discord activity sharing. The public application ID needs no token or login.

> **Never enter your Discord token.** UmaPresence does not need it.

### After a game update

If Hachimi still sends your activity, keep using it. For Standalone, open **Game integration → Check compatibility** after a DMM update. If your game build needs a newer UmaPresence package, check [GitHub Releases](https://github.com/BenDore1810/UmaPresence/releases) for a compatible download.

If detailed presence is unavailable, the game can still run and Discord can show **Playing Umamusume**. If only the language pack changed, use **Data & artwork → Reload translations**.

## Usage

### Concert controls

Concert progress uses the game's playback clock. The bar appears while paused by default. Choose **Always during a concert** or **Off** under **Concert** to change this. When the white handle appears, drag it within the range that the active audio tracks share and release to seek. Windows media controls are independent. Hachimi mode uses Hachimi's concert controls.

In Standalone mode, enable **Use the same Uma in multiple slots** in Concert settings and save, then select duplicates on the game's formation screen. Concert playback uses the game's original arrangement. Experimental audio arrangements and vocal balancing are disabled.

### Custom concert covers

Leave **Presence → Prefer original song covers** checked to use the default artwork. The public download comes with no custom song covers configured.

Follow these steps if you have upload access to the Discord application UmaPresence uses.

1. Open **Data & artwork → Song covers**. To generate a PNG from the concert currently playing, use **Generate current cover** and **Open cover folder**. You can also supply your own PNG.
2. Click **Open Discord assets** and upload the cover to the application that opens.
3. Back in UmaPresence, select the song, enter the uploaded **Discord asset name**, check **I uploaded this asset to this Discord application**, and click **Register uploaded asset**.
4. Open **Presence**, clear **Prefer original song covers**, and click **Save changes**. Songs without a registered cover use the original artwork when available. Discord may take a moment to show the new cover.

## Uninstalling

Close the game before switching integrations or uninstalling. The **Game integration** page has these options.

| Action | Effect |
| --- | --- |
| **Uninstall UmaPresence integration…** | Removes the bridge, restores the previous game loader, and keeps the app available |
| **Full uninstall…** | Also closes the app and permanently removes the extracted package files, settings, generated caches, and sign-in startup entry |
| **Return to stock DMM** | Changes how the game starts and keeps saved Hachimi files for later |

Keep the original ZIP for reinstalling into a fresh folder, and extract each copy into its own folder so its package files can be removed separately from your other files. If removal was interrupted, close the game and click **Full uninstall…** again. An active Hachimi loader and translation files stay in place.

## Troubleshooting and diagnostics

Review any diagnostic report locally before sharing it. Remove game paths, your Windows username, trainer or account identifiers, roster details, and any unrelated personal information. Reports and logs are local files and are not part of the release.

Never attach tokens, original engine DLLs, backups, recordings, or complete diagnostic captures.

## Building from source

See [BUILD.md](BUILD.md).

## Verification

Check ZIP hashes against `SHA256SUMS.txt`. The binary package includes a per-file checksum manifest. The corresponding source is available under GPL-3.0. See [third-party notices](release/NOTICES.md) and [BUILD.md](BUILD.md).

## Acknowledgements

UmaPresence started as an independent take on the Discord Rich Presence feature in KevinVG207's [UmaLauncher](https://github.com/KevinVG207/UmaLauncher#better-discord-rich-presence-for-uma-musume).

## License

Released under the [GPL-3.0 license](LICENSE).
