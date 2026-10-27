using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Copy2FA.Core.Settings;

/// <summary>The modifier keys of a shortcut, with the same values RegisterHotKey uses.</summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>A global keyboard shortcut: modifier keys plus one Windows virtual-key code.</summary>
[JsonConverter(typeof(ShortcutJsonConverter))]
public readonly record struct Shortcut(ShortcutModifiers Modifiers, int Key)
{
    public static readonly Shortcut DefaultCopy = new(ShortcutModifiers.Control | ShortcutModifiers.Shift, 'C');
    public static readonly Shortcut DefaultAuthenticator = new(ShortcutModifiers.Control | ShortcutModifiers.Shift, 'A');

    /// <summary>
    /// Whether the shortcut can be registered without taking over normal typing: it needs
    /// Ctrl, Alt or Windows, unless the key is a function key.
    /// </summary>
    public bool IsValid =>
        KeyName(Key) is not null
        && ((Modifiers & (ShortcutModifiers.Control | ShortcutModifiers.Alt | ShortcutModifiers.Windows)) != 0 || IsFunctionKey(Key));

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key) ?? "?");
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out Shortcut shortcut)
    {
        shortcut = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var modifiers = ShortcutModifiers.None;
        foreach (var part in parts[..^1])
        {
            ShortcutModifiers? modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ShortcutModifiers.Control,
                "alt" => ShortcutModifiers.Alt,
                "shift" => ShortcutModifiers.Shift,
                "win" or "windows" => ShortcutModifiers.Windows,
                _ => null,
            };
            if (modifier is null) return false;
            modifiers |= modifier.Value;
        }
        var key = KeyCode(parts[^1]);
        if (key is null) return false;
        shortcut = new Shortcut(modifiers, key.Value);
        return shortcut.IsValid;
    }

    /// <summary>The label for a virtual-key code, or null for keys a shortcut can't use.</summary>
    public static string? KeyName(int key) => key switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)key).ToString(),
        >= 0x70 and <= 0x87 => "F" + (key - 0x6F).ToString(CultureInfo.InvariantCulture),
        _ => NamedKeys.FirstOrDefault(pair => pair.Value == key).Key,
    };

    static int? KeyCode(string name)
    {
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') return c;
        }
        if (name.Length is 2 or 3 && name[0] is 'F' or 'f'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number is >= 1 and <= 24)
        {
            return 0x6F + number;
        }
        foreach (var pair in NamedKeys)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        }
        return null;
    }

    static bool IsFunctionKey(int key) => key is >= 0x70 and <= 0x87;

    static readonly KeyValuePair<string, int>[] NamedKeys =
    [
        new("Space", 0x20), new("PageUp", 0x21), new("PageDown", 0x22), new("End", 0x23), new("Home", 0x24),
        new("Left", 0x25), new("Up", 0x26), new("Right", 0x27), new("Down", 0x28), new("Insert", 0x2D),
        new("Delete", 0x2E),
    ];
}

internal sealed class ShortcutJsonConverter : JsonConverter<Shortcut>
{
    public override Shortcut Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && Shortcut.TryParse(reader.GetString(), out var shortcut)) return shortcut;
        reader.Skip();
        return default;
    }

    /// <summary>Written raw so "+" stays readable instead of being escaped. Shortcuts only hold letters, digits and "+".</summary>
    public override void Write(Utf8JsonWriter writer, Shortcut value, JsonSerializerOptions options) =>
        writer.WriteRawValue("\"" + value + "\"");
}
