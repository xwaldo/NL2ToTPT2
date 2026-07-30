# Known Issues and Limitations

## Precision drift on long layouts

Small differences from coordinate conversion, decimal parsing, Roblox number
handling, and the game's spline implementation can accumulate across a long
layout. Full input precision removes most importer-side rounding but cannot
remove precision limits inside Roblox or Theme Park Tycoon 2.

Mitigations:

- Keep Full input precision enabled.
- Begin from a grid-aligned starting node.
- Compare expected built positions periodically.
- Split very long layouts into supervised runs.

## A bump can be felt at each section

The imported nodes can be positionally and rotationally aligned while the
game's per-section curve or vehicle physics still produces a noticeable seam.
This is particularly visible in POV with smoothing factor `0`. The exact
Theme Park Tycoon 2 spline equation is not publicly documented.

Mitigations:

- Set node spacing to at least 4 metres in NoLimits 2.
- Test longer spacing on a copy of the layout.
- Compare the result with the game's Auto smooth behavior.
- Use a nonzero POV smoothing factor when appropriate.

## Input can be missed

Roblox may miss a mouse movement, click, textbox update, or decimal point when
the game is lagging. The importer verifies the Windows cursor but cannot read
the custom in-game cursor, textbox focus, or accepted value.

Mitigations:

- Lower Roblox graphics.
- Do not touch the mouse or keyboard during automation.
- Increase input/glide, tab-settle, or build delays above the `20 ms` defaults.
- Keep Roblox visible and use windowed or borderless-windowed mode.
- Supervise the run and use **Ctrl+Shift+X** when anything is wrong.

## Editor warnings interrupt text input

Warnings including **track twists too much**, **track too short**, and **track
too long** can take focus. Once this happens, later input may be typed into the
wrong control or ignored.

Before section 1, ensure the selected starting node matches the yaw, pitch, and
roll in the highlighted **ANCHOR** row. The anchor is point 1; section 1 builds
point 1 to point 2. This is required in both position modes. A mismatched
starting rotation can itself cause a **track twists too much** warning.

If a warning appears, stop immediately, dismiss it, correct the partial
section, and resume using **Start at section**.

## Blueprint section limits

The game can reject a blueprint containing too many track sections. The
importer reports the generated section count but cannot know the remaining
blueprint allowance for a particular layout or game version.

Mitigations:

- Use 4 metres or greater node spacing in NoLimits 2.
- Review and remember the section count before building.
- Use larger spacing or divide the layout when the count is excessive.
