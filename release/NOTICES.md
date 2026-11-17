# Licensing and modifications

UmaPresence is distributed under GNU GPL version 3. See LICENSE. Corresponding source accompanies each binary release; preserve it with redistributed binaries.

The native runtime is a locally maintained adaptation of Hachimi Edge, pinned to 3c48a46925a8f943a80515877a52309e447d2c7f at https://github.com/kairusds/Hachimi-Edge (GPLv3). Pinned consulted source, license, and file hashes are in standalone/upstream/hachimi-edge. Modifications include a restricted service interface, validated build resolution, telemetry integration, main-thread commands, independent concert controls, loader ownership and the lightweight seek overlay. The public build enables opt-in duplicate casting and keeps experimental vocal routing disabled.

MinHook source and its BSD-style license are in standalone/upstream/minhook; the binary package carries MinHook-LICENSE.txt. Original authorship notices remain in source files.

Cellar v2.0.0 is obtained separately from its official distribution, never redistributed here. See https://hachimi.noccu.art/docs/hachimi/installing-windows and https://github.com/Hachimi-Hachimi/Cellar/releases/tag/v2.0.0 . Expected x64 asset SHA-256: f97609628ba41f32c7ffc8674894f2eea100a6bcf10f560da4fbd57b7a2a82cb.

Bundled branding is the project's generated horseshoe icon (assets/generate_icon.py). No extracted character artwork, music, vocal recording or proprietary game executable is bundled. Catalogs describe game identifiers and reference external artwork; review their provenance and external hosting terms before publication. The game and its artwork belong to their respective owners. No endorsement is implied.
