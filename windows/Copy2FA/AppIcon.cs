namespace Copy2FA;

/// <summary>The app icon, built into the executable.</summary>
internal static class AppIcon
{
    public static Icon Load() => Load(new Size(32, 32));

    /// <summary>Picks the size from the icon file closest to <paramref name="size"/>.</summary>
    public static Icon Load(Size size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("Copy2FA.Assets.Copy2FA.ico")
            ?? throw new InvalidOperationException("The app icon is missing from the build.");
        return new Icon(stream, size);
    }
}
