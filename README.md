# NL2 to TPT2

NL2 to TPT2 is a Windows desktop application that converts a NoLimits 2 CSV
track export into section inputs for the Theme Park Tycoon 2 advanced track
editor. It remains external to Roblox and uses standard Windows mouse and
keyboard input.

## Download

Download `NL2ToTPT2-win-x64.zip` from the repository's Releases page, extract
it, and run `NL2ToTPT2.exe`. The packaged release is self-contained and does
not require a separate .NET installation.

## Before using Auto mode

- For TPT2 curve fitting, export a dense NoLimits CSV. **0.2 metres** is a
  useful starting point; the dense rows are reference samples and will not all
  become sections in Roblox.
- Enable **Center of Rails** in the NoLimits 2 CSV export options.
- Prefer starting the export on a grid-aligned right angle so the station can
  be built snapped to the Roblox grid.
- In Roblox, open the ride's **Advanced editor**.
- Turn **Auto smooth off**.
- Disable **every snap option**.
- Set **Translation Space** to the same Position reference selected in the
  importer: World or Previous node.
- Select the existing first track node and rotate it to the yaw, pitch, and
  roll shown in the highlighted **ANCHOR** row. The importer does not build
  this point; section 1 builds point 1 to point 2.
- Lower Roblox graphics if the game is slow to accept input.
- Review and remember the generated section count.

See the complete [User Guide](docs/USER_GUIDE.md) before the first live run.

## Default import settings

| Setting | Default |
| --- | ---: |
| Position scale | `3.6` |
| Yaw modification | `0°` (increment mode) |
| TPT2 curve fit | On |
| Maximum curve error | `0.50` game units |
| TPT2 handle factor | `0.39` |
| Resample points | Off |
| Full input precision | On |
| Position reference | Previous node axes |
| Input/glide time | `50 ms` |
| Tab settle delay | `20 ms` |
| Build delay | `20 ms` |
| Dry-run | Off |

The input/glide default is `50 ms`; tab-settle and build remain `20 ms`.
Increase one or more delays when Roblox misses clicks, textbox changes, or
decimal points.

## Live automation safety

Live mode waits three seconds so you can switch to Roblox. Once it starts:

- do not touch the keyboard or mouse;
- watch the track editor for warnings or missed input; and
- press the emergency stop shortcut shown in Auto mode at any time. It defaults
  to **Ctrl+Shift+X**. If Windows cannot register it, choose another key and
  select **Apply shortcut** before starting.

If a run fails, note the last successfully built section in the log, correct or
remove any partial section, dismiss any Roblox warning, and restart using
**Start at section**.

Warnings such as **track twists too much**, **track too short**, or **track too
long** can take focus and stop the editor from accepting text. Automation
cannot detect that game state, so cancel immediately if one appears.

The selected starting node must match the rotation shown for the starting
point. This applies to both World and Previous node modes. A mismatched anchor
rotation can cause a **track twists too much** warning on the first section.

## Respect Workshop creators

If a layout came from the Steam Workshop, ask its owner for permission before
using the ride in your game. Credit the original creator inside the game using
their Steam username, and include the name of the original project or ride.

## Known issues

- Precision differences can accumulate over long layouts.
- A bump may be felt at every section, especially in POV with smoothing factor
  `0`.
- Roblox can miss input when the game is lagging.
- A generated layout may contain more sections than the game allows in a
  blueprint.

See [Known Issues](docs/KNOWN_ISSUES.md) for details and mitigations.

## Features

- Parses NoLimits 2 tab-separated CSV exports with position and
  Front/Left/Up vectors.
- Provides exact-point, fixed-spacing resampling, and TPT2-aware curve-fitting
  modes.
- Fits simulated TPT2 cubic sections to the ordered dense CSV path, reports
  maximum and RMS deviation, preserves the final endpoint, and limits every
  fitted section to 95 scaled game units.
- Keeps curved and rolling sections near a 15-game-unit target and checks both
  centerline and roll interpolation. Only detected long straights with
  near-zero direction and roll rate may expand toward 95 units.
- Enforces a hard 4-game-unit minimum and prefers at least 6 units, even when a
  stricter curve tolerance would otherwise request smaller sections.
- Measures banking density from continuous orientation-frame twist rather than
  Euler roll deltas, preventing false short sections near vertical pitch while
  preserving genuine vertical rolls.
- Supports world-axis and previous-node position references.
- Applies configurable position scale and coordinate conversion.
- Rotates the complete import around Point 1 by a yaw increment or sets the
  anchor to an exact requested yaw.
- Supports full-precision or fixed-precision position and rotation input.
- Previews every relative section and expected built position.
- Highlights point 1 as the anchor and shows the rotation required before
  section 1 builds point 1 to point 2.
- Saves normalized Roblox control calibration per Windows user.
- Supports resume from a selected section.
- Provides dry-run, pause, Stop, and global emergency stop controls.
- Uses serialized, verified 60 Hz mouse trajectories and standard Windows
  `SendInput`.

Calibration is stored at
`%LocalAppData%\NL2ToTPT2\calibration.json`.

## Development

Requires Windows and the .NET 10 SDK.

```powershell
dotnet restore NL2ToTPT2.slnx
dotnet build NL2ToTPT2.slnx -c Release
dotnet tests/Importer.Core.Tests/bin/Release/net10.0-windows/Importer.Core.Tests.dll
dotnet run --project src/Importer.UI/Importer.UI.csproj
```

The real-input test launches an instrumented WPF editor stand-in and controls
the local mouse and keyboard:

```powershell
dotnet run --project tests/Importer.InputIntegrationTests/Importer.InputIntegrationTests.csproj -c Release -- --iterations 10
```

Do not use the mouse or keyboard while that integration test is active.

## Creating a release

The GitHub Actions workflow builds and uploads a Windows x64 ZIP on pushes,
pull requests, and manual runs. Pushing a version tag creates a GitHub Release:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Project note

This project was created with assistance from AI. Its generated code and
documentation have been reviewed and tested through the repository's automated
and real-input test harnesses, but users should supervise live automation.
