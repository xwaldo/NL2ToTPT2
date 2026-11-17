# Changelog

## 0.3.1

Fixed FVD CSV imports by adding support for long straight sections.

## 0.1.0

First public release.

- NoLimits 2 CSV/TSV parsing and preview.
- Exact-point and optional resampled generation.
- World-axis and previous-node position references.
- Full-precision position and absolute rotation input.
- Saved Roblox UI calibration.
- Resumable, supervised live automation with a global emergency stop.
- Instrumented Windows-input integration test.
- In-app Guide and Known Issues tabs.
- Previous-node position mode is the default.
- Point 1 is shown as a highlighted anchor, with section 1 explicitly building
  point 1 to point 2.
- The setup guide now covers Center of Rails exports and Workshop creator
  permission and credit.
- Input/glide now defaults to 50 ms; build and tab-settle remain at 20 ms.
- Regression tests no longer depend on optional large CSV fixtures.
- Added optional TPT2-aware adaptive curve fitting for dense NoLimits exports.
  It simulates equal-handle cubic sections with a configurable measured handle
  factor, enforces maximum path deviation and a 95-unit scaled chord cap,
  preserves the ordered final endpoint, and reports maximum/RMS fit error.
- Curve fitting keeps curved and rolling sections near a 15-game-unit target,
  measures roll progression as well as centerline error, and permits shorter
  sections when accuracy requires them. The up-to-95-unit optimization is
  restricted to detected long straights with near-zero direction and roll
  rate.
- Fitted sections now have a hard four-unit minimum and a six-unit preferred
  safety floor. The minimum wins when an unusually strict fit tolerance would
  otherwise create an unbuildably short section.
- Curve-fit roll decisions now use parallel-transported orientation-frame
  twist. This avoids unnecessary tiny sections from Euler yaw/roll ambiguity
  near vertical pitch without changing the rotations sent to Roblox.
