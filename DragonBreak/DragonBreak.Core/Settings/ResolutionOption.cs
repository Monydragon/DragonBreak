namespace DragonBreak.Core.Settings;

public readonly record struct ResolutionOption(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";
}

