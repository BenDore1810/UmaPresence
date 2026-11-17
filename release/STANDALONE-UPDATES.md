# Standalone compatibility updates

The public build can pin one official HTTPS feed. Supply a UTF-8 JSON file using `-CompatibilityFeedConfig` with exactly three public values.

| Key | Value |
| --- | --- |
| `ManifestUrl` | The direct HTTPS address of `manifest.json` on the approved release site. The endpoint and archive downloads must respond without redirects. |
| `Modulus` | Base64-encoded unsigned big-endian RSA modulus, 2048 to 4096 bits. |
| `Exponent` | `AQAB`, the base64 encoding of 65537. |

The signing key stays outside the workspace, built app, and release archives. Host the manifest and its bundles in the same directory. Use a static HTTPS release endpoint so a redirect cannot change where the app connects. The app performs local checks without a feed configured. A build with no pinned feed cannot download an update.

## Publishing a bundle

1. Compile and test the Standalone loader, bridge, and game profile against the target game and companion. Record which features worked.
2. ZIP exactly `UnityPlayer.dll`, `umapyon_bridge.dll`, and `game-profile.json` at the archive root. These DLLs are UmaPresence builds. Do not include the official game engine, GameAssembly, Hachimi, or the separately verified Cellar helper.
3. Name the ZIP `standalone-<Sequence>.zip`. The positive integer sequence increases across the entire feed and is never reused. Upload the complete ZIP before publishing a manifest that references it.
4. Write a UTF-8 manifest payload with the following fields. Fingerprints use lowercase SHA-256 hex. The game profile's exact hashes must equal the manifest's game hashes.

| Field | Value |
| --- | --- |
| `Format` | `1` |
| `Product` | `UmaPresence Standalone compatibility` |
| `ExpiresUtc` | A future UTC timestamp in .NET round-trip format with seven fractional digits and offset `+00:00`. |
| `Releases` | Array of at most 256 entries. An entry has `Sequence`, `Version`, `CompanionVersion`, `UnitySha256`, `AssemblySha256`, `ArchiveSha256`, `Files`, and `Features`. |
| `Files` values | Map the three ZIP filenames to the SHA-256 digest of their decoded bytes. |
| `Features` values | Include `Detailed presence`. Add `Song bar` and `Duplicate casting` only if those features passed live tests. |

5. Sign the exact payload bytes with RSA PKCS #1 v1.5 and SHA-256. Publish an outer JSON object in `manifest.json` with `Manifest` containing base64 of the unsigned payload bytes and `Signature` containing base64 of the raw signature bytes. The app validates the payload before selecting an entry. Keep the resulting envelope under 256 KiB and each ZIP under 20 MiB.
6. Check the feed from the candidate on the declared DMM build. Test install, launching until bridge activity appears, and recovery. Bundles for different game or companion versions must remain unavailable in the current install.

The app keeps the current Standalone profile as the immediate rollback copy. It can restore that copy only if it still matches the current game. Installation with Hachimi active updates the inactive Standalone profile and leaves Hachimi active. An interruption before activation completes can be reversed through **Recover interrupted switch**.

Run `UmapyonPresence.Tests.exe --standalone-update-regression` for the offline signed-feed fixture. It generates a disposable signing key, exercises the verifier and replacement journal, and does not contact a server or load a game.
