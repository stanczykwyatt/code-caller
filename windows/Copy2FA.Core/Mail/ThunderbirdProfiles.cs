namespace Copy2FA.Core.Mail;

/// <summary>Finds the mbox files that hold Thunderbird's inboxes and junk folders.</summary>
public static class ThunderbirdProfiles
{
    static readonly string[] InboxNames = ["Inbox"];
    static readonly string[] JunkNames = ["Junk", "Spam", "Junk E-mail", "Junk Email", "Bulk Mail"];

    /// <summary>
    /// Every inbox and junk folder in every profile listed in <c>profiles.ini</c> under
    /// <paramref name="thunderbirdDirectory"/>, which is <c>%APPDATA%\Thunderbird</c> on Windows.
    /// </summary>
    public static List<string> FindMailboxes(string thunderbirdDirectory) =>
        ProfileDirectories(thunderbirdDirectory).SelectMany(MailboxFiles).ToList();

    public static List<string> ProfileDirectories(string thunderbirdDirectory)
    {
        var ini = Path.Combine(thunderbirdDirectory, "profiles.ini");
        if (!File.Exists(ini)) return [];

        var found = new List<string>();
        foreach (var section in ReadIni(File.ReadAllLines(ini)))
        {
            string? path;
            bool? isRelative = null;
            if (section.Name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase))
            {
                section.Values.TryGetValue("Path", out path);
                if (section.Values.TryGetValue("IsRelative", out var relative)) isRelative = relative.Trim() == "1";
            }
            else if (section.Name.StartsWith("Install", StringComparison.OrdinalIgnoreCase))
            {
                // Newer versions name each installation's default profile here: relative unless it's a full path.
                section.Values.TryGetValue("Default", out path);
            }
            else
            {
                continue;
            }
            if (string.IsNullOrWhiteSpace(path)) continue;

            path = path.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var full = isRelative == false || (isRelative is null && Path.IsPathRooted(path))
                ? path
                : Path.Combine(thunderbirdDirectory, path);
            full = Path.GetFullPath(full);
            if (Directory.Exists(full) && !found.Contains(full, StringComparer.OrdinalIgnoreCase)) found.Add(full);
        }
        return found;
    }

    /// <summary>The inbox and junk mbox files of each account in a profile, local folders included.</summary>
    public static List<string> MailboxFiles(string profileDirectory)
    {
        var files = new List<string>();
        foreach (var store in new[] { "Mail", "ImapMail" })
        {
            var storeDirectory = Path.Combine(profileDirectory, store);
            if (!Directory.Exists(storeDirectory)) continue;
            foreach (var account in SafeDirectories(storeDirectory))
            {
                AddFiles(files, account, [.. InboxNames, .. JunkNames]);
                // Gmail keeps its spam folder one level down, in "[Gmail].sbd".
                foreach (var folder in SafeDirectories(account))
                {
                    var name = Path.GetFileName(folder);
                    if (name.EndsWith(".sbd", StringComparison.OrdinalIgnoreCase)
                        && !name.Equals("INBOX.sbd", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFiles(files, folder, JunkNames);
                    }
                }
            }
        }
        return files;
    }

    static void AddFiles(List<string> files, string directory, string[] names)
    {
        IEnumerable<string> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return;
        }
        foreach (var file in candidates)
        {
            // Folders are mbox files with no extension; ".msf" files beside them are only indexes.
            var name = Path.GetFileName(file);
            if (names.Any(wanted => wanted.Equals(name, StringComparison.OrdinalIgnoreCase))) files.Add(file);
        }
    }

    static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal sealed record IniSection(string Name, Dictionary<string, string> Values);

    internal static List<IniSection> ReadIni(IEnumerable<string> lines)
    {
        var sections = new List<IniSection>();
        IniSection? current = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line[0] == '[' && line[^1] == ']')
            {
                current = new IniSection(line[1..^1].Trim(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                sections.Add(current);
                continue;
            }
            var equals = line.IndexOf('=');
            if (current is null || equals <= 0) continue;
            current.Values[line[..equals].Trim()] = line[(equals + 1)..].Trim();
        }
        return sections;
    }
}
