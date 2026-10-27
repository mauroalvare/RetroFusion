namespace Retro.App;

/// <summary>
/// Optional pause while the window is in the background. It resumes only a pause it made itself:
/// a game the player paused, or resumed by hand meanwhile, is left as the player chose.
/// </summary>
public sealed class FocusPause
{
    public enum Step { None, Pause, Resume }
    private bool _playerResumed;
    public bool PausedByFocus { get; private set; }

    public Step Update(bool enabled, bool focused, bool running, bool paused)
    {
        if (focused) _playerResumed = false;
        if (PausedByFocus && !running) PausedByFocus = false;   // the library opened or the game changed
        if (PausedByFocus && !paused)
        {
            // Resumed by the player (a gamepad works without focus): not paused again until the focus returns.
            PausedByFocus = false;
            _playerResumed = !focused;
        }
        if (PausedByFocus)
        {
            if (!focused) return Step.None;
            PausedByFocus = false;
            return Step.Resume;
        }
        if (!enabled || focused || !running || paused || _playerResumed) return Step.None;
        PausedByFocus = true;
        return Step.Pause;
    }
}
