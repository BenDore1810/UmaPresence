# Build the corresponding source

Windows x64 build tools used for this candidate: .NET SDK 9.0.202 (Roslyn), .NET Framework 4.5.2 reference assemblies and .NET Framework 4.x Windows runtime, Windows 10 SDK 10.0.22000.0 libraries/metadata, Zig 0.13.0 and Python 3 standard library. Pillow is optional only to regenerate the already supplied generated horseshoe icon.

Run release/build.ps1 with -CompilerPath pointing to Zig and -SdkLibraryPath pointing to the SDK um/x64 library directory. Optionally supply -HelperPath with the pinned official Cellar file to run public installer fixtures; it is verified before being cached locally and is never packaged. Neither tool nor SDK is bundled. build.ps1 discovers .NET SDK and Framework references from installed Windows locations. Output goes under dist; no personal game folder is needed for the shipped generated supported-build inputs. Do not regenerate profiles against unknown builds and treat that as acceptance.

The packager uses release/source-files.json and release/binary-files.json as exact allowlists. Unexpected staged files or privacy findings fail the build. Source identity is the SHA-256 of its path/hash listing, not a private Git history. No diagnostic captures or private fixtures are distributed.

Public checks: run dist/public-build/UmapyonPresence.Tests.exe --public-release . Supply an optional separately downloaded official helper path to verify positive and tamper-rejection cases. Historic fixture tests reference private captures and are not the release acceptance suite. Offline tests are not evidence of live audio correctness.

Public native builds use UMAPRESENCE_PUBLIC_RELEASE and companion builds use PUBLIC_RELEASE. Always rebuild both before packaging. The packager creates binary/source ZIPs from fresh staging directories, checks nested archives and UTF-16 strings, and records unresolved acceptance checks. Publication is blocked until every enabled-feature acceptance item is passed for those exact hashes.
