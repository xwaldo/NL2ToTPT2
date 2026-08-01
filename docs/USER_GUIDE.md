# User Guide

This guide assumes you have a finished track in NoLimits 2 and want to rebuild
its shape in Theme Park Tycoon 2. You do not need to know anything about
programming.

## Before you begin

The importer controls your real mouse and keyboard. Stay at the computer and
watch the build. Do not touch the mouse or keyboard while it is working.

Press **Ctrl+Shift+X** at any time to stop it.

## Step 1: Export the track from NoLimits 2

1. Open the CSV export window in NoLimits 2.
2. Turn on **Center of Rails**.
3. For **TPT2 curve fit**, export densely. Start with node spacing around
   **0.5 metres**. These rows are reference samples; the importer selects a
   much smaller set of sections to build.
4. If possible, start the export on a straight section facing a grid-aligned
   right angle. This makes it easier to place a station on the Roblox grid.
5. Export the track as a CSV file.

More source samples let the importer measure the original B-spline accurately
between candidate TPT2 nodes. They do not force the importer to build every
exported row.

## Step 2: Open the CSV in NL2 to TPT2

1. Open `NL2ToTPT2.exe`.
2. On the **Import** tab, choose **Select CSV**.
3. Select the file you exported.
4. Leave these recommended defaults unless you have a reason to change them:
   - Position scale: `3.6`
   - Full input precision: on
   - Max curve error: `0.50`
   - Handle factor: `0.39`
   - Average target length: `15` game units
   - Hard minimum length: `4` game units
   - Position reference: Previous node axes
5. Select **Generate preview**.

The preview shows one highlighted blue row followed by the sections that will
be built. Curve fitting is always applied. The status line reports maximum/RMS
deviation and actual minimum/average/maximum section length. Every fitted
section is limited to 95 game units. Curved and rolling sections stay near the
configurable average target. Only a
detected long straight with near-zero direction and roll rate may use longer
sections. Fitted sections never go below the configured hard minimum; this
minimum takes priority if the requested path tolerance would require a shorter
section. Raising the minimum can therefore increase path error.

## Step 3: Set up the anchor

The highlighted **ANCHOR** row is Point 1, the first point in the CSV. It is the
starting track node that already exists in Roblox. Automation does not build
this point.

1. In Roblox, select the node where the imported track should begin.
2. Open the ride's **Advanced editor**.
3. Reset the selected starting section to a **straight shape**. Do this before
   applying the anchor rotation to reduce **track twists too much** warnings.
4. Find the yaw, pitch, and roll shown in the blue ANCHOR row.
5. Rotate the selected Roblox node to those exact values.

Section 1 builds **Point 1 → Point 2**. Section 2 builds **Point 2 → Point 3**,
and so on.

The anchor rotation is required in both World and Previous node modes. If it
does not match, Roblox may show **track twists too much** on the first section.

## Step 4: Prepare the Roblox editor

Before starting, check all of the following:

1. **Advanced editor** is open.
2. **Auto smooth** is off.
3. Every position and rotation snap option is off.
4. **Translation Space** matches the Position reference in NL2 to TPT2:
   - Choose **Previous node** for Previous node axes.
   - Choose **World** for World axes.
5. Roblox is windowed or borderless-windowed.
6. Roblox graphics are lowered if the game is running slowly.

Remember the number of sections shown below the preview. You may need it if a
run has to be restarted.

## Step 5: Calibrate Auto mode

1. Select **Auto mode**.
2. Start with **Move spline tab**. Select Capture, move the cursor over that
   tab in Roblox, and leave it there for three seconds.
3. Capture Position X, Y, and Z the same way.
4. Capture **Rotate spline tab**, then Yaw, Pitch, and Roll.
5. Capture the Build button.
6. For Focus release, capture an empty area that does nothing when clicked.

Calibration is saved automatically, so it normally only needs to be done again
if the Roblox editor layout or window size changes.

The default timing profile uses 50 ms mouse movement, 50 ms target hold, 40 ms
mouse-button timing, and 20 ms for text, tab, and build settling. If Roblox
misses input, increase only the relevant setting:

- **Mouse movement**: the custom cursor does not finish approaching the target.
- **Target hold**: the cursor reaches the target but Roblox reacts late.
- **Mouse button**: clicks or double-click selection are missed.
- **Text step**: clearing, a number, decimal point, or Enter is missed.
- **Tab settle**: a value goes into the wrong position/rotation group.
- **Build settle**: the next section starts before Roblox finishes building.

Start with small increases. Raising every delay multiplies the build time.

## Step 6: Start building

1. For a new track, leave **Start at section** set to `1`.
2. Leave Dry-run off.
3. Select **Start**.
4. Read the final message carefully. It repeats the point you must select and
   the rotation that point needs.
5. Select Yes and switch to Roblox during the three-second countdown.
6. Take your hands off the mouse and keyboard.
7. Watch the track continuously.

If Roblox shows **track twists too much**, **track too short**, or **track too
long**, press **Ctrl+Shift+X** immediately. These messages can stop the game
from accepting later numbers.

## Step 7: Continue after a failure

1. Press **Ctrl+Shift+X**.
2. Read the import log and find the last section that completed.
3. Dismiss any Roblox warning.
4. Remove or correct the unfinished section.
5. Select the last good node in Roblox.
6. Set **Start at section** to the first section that still needs to be built.
7. Check the point and rotation shown in the final start message.
8. Increase a delay if game lag caused the problem, then start again.

## Workshop layouts

If the layout came from the Steam Workshop, ask its owner for permission before
using the ride in your game. Credit the original creator inside the game using
their Steam username, and include the name of the original project or ride.
