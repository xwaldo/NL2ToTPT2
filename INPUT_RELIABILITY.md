# Input reliability investigation

## Finding

The current live automation is open-loop. It can prove that:

- Roblox is the foreground top-level window at the start of a section;
- Windows moved the cursor to the requested screen coordinate;
- `SendInput` accepted every requested input event; and
- the runner awaited its configured delays.

It cannot prove that Roblox:

- processed the mouse click during the expected game frame;
- focused the intended in-game textbox;
- applied the complete text value to that textbox;
- activated the requested Move or Rotate tab; or
- completed Build.

Microsoft documents `SendInput` as serial insertion into the Windows keyboard
and mouse input stream. Its return value is the number of events inserted, not
an acknowledgement from the receiving application's controls:

<https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput>

Foreground activation is also restricted by Windows:

<https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow>

## Evidence

The observed failures are one-field shifts:

- intended Y appears in X and intended Z appears in Y;
- intended pitch appears in yaw and intended roll appears in pitch.

That pattern means input values remain ordered, but the target focus or target
hit-test is one action behind. It is not consistent with the CSV conversion
swapping values.

An instrumented WPF target now tests the production
`WindowsInputSimulator`. It records:

- mouse-down and click count;
- actual keyboard focus and blur;
- text changes;
- key-down events and Enter commits;
- active tab changes; and
- all six field values observed at Build.

Ten traced iterations produced 60 correctly ordered focus events and 60
correctly ordered commits. A separate automatically validated three-iteration
run matched all six expected values in every Build snapshot. This demonstrates
that the serialized Windows input path is ordered against a native observable
target. It does not demonstrate that Roblox's custom in-game controls consumed
the events.

## Why the recent approaches failed

### Longer delays

A delay lowers the probability of overrunning a busy game frame, but it cannot
confirm focus. A missed click remains undetected.

### Repeated tab clicks

Blind retries repeat an action without checking its result. If the cursor or UI
state is stale, the same wrong control is clicked repeatedly.

### Continuous mouse heartbeat

A background sender introduces a second source of mouse events. A previous
target can race the next serialized action, so visible cursor state and runner
state no longer have a single owner.

### Intent-only logging

Logging "entering Y" records what the runner requested, not the textbox that
received the value. Completed-operation logging is more accurate, but still
cannot identify an in-game textbox without target feedback.

## Replacement architecture

Live automation should become a closed-loop state machine:

1. Confirm the Roblox top-level window is foreground and its client bounds
   still match calibration.
2. Move to one calibrated control and click once.
3. Observe the corresponding game state.
4. Continue only after the expected field or tab is visibly active.
5. Enter one whole formatted value.
6. Observe and verify that value in the intended field.
7. Commit with Enter.
8. Verify focus release or the committed visual state.
9. Activate Build once and verify a build-state visual change.
10. Retry only the failed, observed transition with a strict retry limit.

Each action must have exactly one owner and one completion condition. No
background cursor movement should run concurrently with clicks or text entry.

## Required calibration changes

Point-only calibration is insufficient for verification. A reliable profile
will need:

- the rectangle of each textbox, not only its center;
- a focused/unfocused visual sample for each textbox style;
- active/inactive visual samples for Move and Rotate;
- a visual region that changes after Build; and
- the Roblox client size and display scaling used during capture.

The verifier can initially use small pixel regions and image-difference
thresholds. If the text is clearly rendered, Windows OCR can later verify the
formatted value itself. Verification must be scoped to the intended field
rectangle so a value in the previous field cannot pass.

## Safe fallback

If Roblox does not expose stable visual differences, fully automatic mode
cannot be made trustworthy with blind `SendInput` alone. The safe fallback is a
supervised mode:

- perform one field or button action;
- pause;
- show the intended value and target;
- require user confirmation before advancing.

This is slower, but it prevents one missed focus transition from corrupting all
subsequent fields.

## Test environment

Build:

```powershell
dotnet build NL2ToTPT2.slnx -c Release
```

Run the real desktop-input integration test:

```powershell
dotnet run --project tests/Importer.InputIntegrationTests/Importer.InputIntegrationTests.csproj -c Release -- --iterations 10
```

Traces are written to `TestResults/InputIntegration/<session>/events.jsonl`.
