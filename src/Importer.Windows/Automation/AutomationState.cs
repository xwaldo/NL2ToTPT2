namespace Importer.Windows.Automation;

public enum AutomationState
{
    Idle,
    LocateWindow,
    FocusWindow,
    ReleaseTextboxFocus,
    SwitchToPositionMode,
    EnterX,
    EnterY,
    EnterZ,
    SwitchToRotationMode,
    EnterYaw,
    EnterPitch,
    EnterRoll,
    Build,
    WaitForCompletion,
    Verify,
    Advance,
    Paused,
    Error,
    Stopped,
    Completed,
}
