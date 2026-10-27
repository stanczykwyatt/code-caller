using CodeCaller.Core;
using Microsoft.Win32;

namespace CodeCaller;

/// <summary>Colors and fonts that follow Windows' light or dark app mode.</summary>
internal sealed class Theme
{
    Theme(bool isDark)
    {
        IsDark = isDark;
        if (isDark)
        {
            Background = Color.FromArgb(0x2B, 0x2B, 0x2B);
            Border = Color.FromArgb(0x45, 0x45, 0x45);
            Text = Color.White;
            SecondaryText = Color.FromArgb(0xC8, 0xC8, 0xC8);
            Field = Color.FromArgb(0x1F, 0x1F, 0x1F);
            Selection = Color.FromArgb(0x3D, 0x3D, 0x3D);
            Success = Color.FromArgb(0x6C, 0xCB, 0x5F);
            Caution = Color.FromArgb(0xFC, 0xB0, 0x45);
        }
        else
        {
            Background = Color.FromArgb(0xF9, 0xF9, 0xF9);
            Border = Color.FromArgb(0xDC, 0xDC, 0xDC);
            Text = Color.FromArgb(0x1A, 0x1A, 0x1A);
            SecondaryText = Color.FromArgb(0x5F, 0x5F, 0x5F);
            Field = Color.White;
            Selection = Color.FromArgb(0xE6, 0xE6, 0xE6);
            Success = Color.FromArgb(0x0F, 0x7B, 0x0F);
            Caution = Color.FromArgb(0x9D, 0x5D, 0x00);
        }
    }

    public bool IsDark { get; }
    public Color Background { get; }
    public Color Border { get; }
    public Color Text { get; }
    public Color SecondaryText { get; }
    public Color Field { get; }
    public Color Selection { get; }
    public Color Success { get; }
    public Color Caution { get; }

    /// <summary>The theme apps are using right now. Read fresh each time, so a change takes effect at once.</summary>
    public static Theme Current => new(AppsUseDarkMode());

    static bool AppsUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    public Color Tint(NoticeSymbol symbol) => symbol switch
    {
        NoticeSymbol.Copied => Success,
        NoticeSymbol.Warning or NoticeSymbol.Lock => Caution,
        _ => SecondaryText,
    };

    /// <summary>Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on Windows 10. Both use the same code points.</summary>
    public static readonly string SymbolFont = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets") ?? "Segoe UI Symbol";

    /// <summary>A monospaced font for codes, so 0 and O, or 1 and l, never look alike.</summary>
    public static readonly string CodeFont = FirstInstalled("Cascadia Mono", "Consolas") ?? FontFamily.GenericMonospace.Name;

    public static string Glyph(NoticeSymbol symbol) => symbol switch
    {
        NoticeSymbol.Copied => char.ConvertFromUtf32(0xEC61),  // CompletedSolid
        NoticeSymbol.Search => char.ConvertFromUtf32(0xE721),  // Search
        NoticeSymbol.Lock => char.ConvertFromUtf32(0xE72E),    // Lock
        NoticeSymbol.Mail => char.ConvertFromUtf32(0xE715),    // Mail
        NoticeSymbol.Warning => char.ConvertFromUtf32(0xE7BA), // Warning
        _ => "",
    };

    static string? FirstInstalled(params string[] names)
    {
        using var fonts = new System.Drawing.Text.InstalledFontCollection();
        foreach (var name in names)
        {
            if (fonts.Families.Any(family => family.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) return name;
        }
        return null;
    }
}
