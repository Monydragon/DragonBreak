#nullable enable
namespace DragonBreak.Core.Settings;

public enum ContinueMode
{
    /// <summary>
    /// After clearing a level, immediately start the next level (still requires serving).
    /// </summary>
    Auto,

    /// <summary>
    /// After clearing a level, show an interstitial and require confirm to continue.
    /// </summary>
    Prompt,

    /// <summary>
    /// Show an interstitial, but auto-continue after a timer.
    /// </summary>
    PromptThenAuto,
}

