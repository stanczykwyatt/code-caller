namespace Copy2FA.Core.Authenticator;

public static class CodeFormat
{
    /// <summary>"482 913", which is easier to read at a glance. The copied code has no space.</summary>
    public static string Grouped(string code)
    {
        if (code.Length < 6) return code;
        var middle = code.Length / 2;
        return code[..middle] + " " + code[middle..];
    }
}
