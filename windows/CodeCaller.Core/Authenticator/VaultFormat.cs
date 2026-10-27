using System.Buffers;
using System.Text.Json;

namespace CodeCaller.Core.Authenticator;

/// <summary>
/// Turns the account list into bytes and back. The Windows app encrypts these bytes
/// before they touch the disk; this class only decides their shape.
/// </summary>
public static class VaultFormat
{
    const int Version = 1;

    public static byte[] Serialize(IReadOnlyList<OtpAccount> accounts)
    {
        // Sized up front so the buffer never grows, which would leave copies of secrets behind.
        var buffer = new ArrayBufferWriter<byte>(256 + accounts.Count * 512);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteStartArray("accounts");
            foreach (var account in accounts)
            {
                writer.WriteStartObject();
                writer.WriteString("id", account.Id);
                writer.WriteString("issuer", account.Issuer);
                writer.WriteString("name", account.Name);
                writer.WriteString("secret", Base32.Encode(account.Secret));
                writer.WriteString("algorithm", OtpAccount.AlgorithmName(account.Algorithm));
                writer.WriteNumber("digits", account.Digits);
                writer.WriteNumber("period", account.Period);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        var bytes = buffer.WrittenSpan.ToArray();
        buffer.Clear();
        return bytes;
    }

    /// <exception cref="FormatException">The bytes aren't a vault this version understands.</exception>
    public static List<OtpAccount> Deserialize(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes.AsMemory());
            var root = document.RootElement;
            if (!root.TryGetProperty("version", out var version) || version.GetInt32() != Version)
            {
                throw new FormatException("Unknown vault version.");
            }
            var accounts = new List<OtpAccount>();
            foreach (var item in root.GetProperty("accounts").EnumerateArray())
            {
                if (!OtpAccount.TryParseAlgorithm(item.GetProperty("algorithm").GetString(), out var algorithm))
                {
                    throw new FormatException("Unknown algorithm.");
                }
                var account = OtpAccount.Create(
                    item.GetProperty("issuer").GetString(),
                    item.GetProperty("name").GetString(),
                    Base32.Decode(item.GetProperty("secret").GetString() ?? ""),
                    algorithm,
                    item.GetProperty("digits").GetInt32(),
                    item.GetProperty("period").GetInt32(),
                    item.GetProperty("id").GetGuid()) ?? throw new FormatException("Invalid account.");
                accounts.Add(account);
            }
            return accounts;
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("The vault is damaged.", error);
        }
    }
}
