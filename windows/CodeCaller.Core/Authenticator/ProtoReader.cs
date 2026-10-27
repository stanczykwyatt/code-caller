using System.Text;

namespace CodeCaller.Core.Authenticator;

/// <summary>Just enough of the protocol buffer wire format to read a Google Authenticator export.</summary>
internal ref struct ProtoReader
{
    public const int Varint = 0;
    public const int Fixed64 = 1;
    public const int LengthDelimited = 2;
    public const int Fixed32 = 5;

    readonly ReadOnlySpan<byte> data;
    int position;

    public ProtoReader(ReadOnlySpan<byte> data)
    {
        this.data = data;
        position = 0;
    }

    /// <summary>Reads the next field's number and wire type, or returns false at the end.</summary>
    public bool TryReadTag(out int field, out int wireType)
    {
        if (position >= data.Length)
        {
            field = 0;
            wireType = 0;
            return false;
        }
        var tag = ReadVarint();
        field = (int)(tag >> 3);
        wireType = (int)(tag & 7);
        if (field == 0) throw new FormatException("Field number zero.");
        return true;
    }

    public ulong ReadVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= data.Length) throw new FormatException("Truncated varint.");
            var b = data[position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return result;
        }
        throw new FormatException("Varint too long.");
    }

    public ReadOnlySpan<byte> ReadBytes()
    {
        var length = ReadVarint();
        if (length > (ulong)(data.Length - position)) throw new FormatException("Truncated field.");
        var bytes = data.Slice(position, (int)length);
        position += (int)length;
        return bytes;
    }

    public string ReadString() => Encoding.UTF8.GetString(ReadBytes());

    public void Skip(int wireType)
    {
        switch (wireType)
        {
            case Varint: ReadVarint(); break;
            case Fixed64: Advance(8); break;
            case LengthDelimited: ReadBytes(); break;
            case Fixed32: Advance(4); break;
            default: throw new FormatException("Unknown wire type.");
        }
    }

    void Advance(int count)
    {
        if (count > data.Length - position) throw new FormatException("Truncated field.");
        position += count;
    }
}
