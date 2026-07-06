using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PS5MemoryPeeker;

public static class MemoryValueCodec
{
    public static readonly string[] ValueTypes =
    [
        "1 byte",
        "2 bytes",
        "4 bytes",
        "8 bytes",
        "Float",
        "Double",
        "Hex",
        "String"
    ];

    public static readonly string[] FirstScanTypes =
    [
        "Exact Value",
        "Fuzzy Value",
        "Bigger Than",
        "Smaller Than",
        "Between Value",
        "Unknown Initial Value"
    ];

    public static readonly string[] NextScanTypes =
    [
        "Exact Value",
        "Fuzzy Value",
        "Increased Value",
        "Increased Value By",
        "Decreased Value",
        "Decreased Value By",
        "Bigger Than",
        "Smaller Than",
        "Changed Value",
        "Unchanged Value",
        "Between Value"
    ];

    public static MemoryValueKind FromDisplayName(string value) => value switch
    {
        "1 byte" => MemoryValueKind.Byte,
        "2 bytes" => MemoryValueKind.UInt16,
        "4 bytes" => MemoryValueKind.UInt32,
        "8 bytes" => MemoryValueKind.UInt64,
        "Float" => MemoryValueKind.Float,
        "Double" => MemoryValueKind.Double,
        "Hex" => MemoryValueKind.Hex,
        "String" => MemoryValueKind.Text,
        "Pointer" => MemoryValueKind.Pointer,
        _ => MemoryValueKind.UInt32
    };

    public static string ToDisplayName(MemoryValueKind value) => value switch
    {
        MemoryValueKind.Byte => "1 byte",
        MemoryValueKind.UInt16 => "2 bytes",
        MemoryValueKind.UInt32 => "4 bytes",
        MemoryValueKind.UInt64 => "8 bytes",
        MemoryValueKind.Float => "Float",
        MemoryValueKind.Double => "Double",
        MemoryValueKind.Hex => "Hex",
        MemoryValueKind.Text => "String",
        MemoryValueKind.Pointer => "Pointer",
        _ => "4 bytes"
    };

    public static ScanCompareKind CompareFromDisplayName(string value) => value switch
    {
        "Fuzzy Value" => ScanCompareKind.FuzzyValue,
        "Increased Value" => ScanCompareKind.IncreasedValue,
        "Increased Value By" => ScanCompareKind.IncreasedBy,
        "Decreased Value" => ScanCompareKind.DecreasedValue,
        "Decreased Value By" => ScanCompareKind.DecreasedBy,
        "Bigger Than" => ScanCompareKind.BiggerThan,
        "Smaller Than" => ScanCompareKind.SmallerThan,
        "Changed Value" => ScanCompareKind.ChangedValue,
        "Unchanged Value" => ScanCompareKind.UnchangedValue,
        "Between Value" => ScanCompareKind.BetweenValue,
        "Unknown Initial Value" => ScanCompareKind.UnknownInitialValue,
        _ => ScanCompareKind.ExactValue
    };

    public static int GetSize(MemoryValueKind kind, string value)
    {
        return kind switch
        {
            MemoryValueKind.Byte => 1,
            MemoryValueKind.UInt16 => 2,
            MemoryValueKind.UInt32 => 4,
            MemoryValueKind.UInt64 or MemoryValueKind.Pointer => 8,
            MemoryValueKind.Float => 4,
            MemoryValueKind.Double => 8,
            MemoryValueKind.Text => Math.Max(1, Encoding.UTF8.GetByteCount(value)),
            MemoryValueKind.Hex => Math.Max(1, NormalizeHex(value).Length / 2),
            _ => 4
        };
    }

    public static byte[] ToBytes(MemoryValueKind kind, string value)
    {
        return kind switch
        {
            MemoryValueKind.Byte => [(byte)ParseInteger(value)],
            MemoryValueKind.UInt16 => BitConverter.GetBytes((ushort)ParseInteger(value)),
            MemoryValueKind.UInt32 => BitConverter.GetBytes((uint)ParseInteger(value)),
            MemoryValueKind.UInt64 or MemoryValueKind.Pointer => BitConverter.GetBytes(ParseInteger(value)),
            MemoryValueKind.Float => BitConverter.GetBytes(float.Parse(value, CultureInfo.InvariantCulture)),
            MemoryValueKind.Double => BitConverter.GetBytes(double.Parse(value, CultureInfo.InvariantCulture)),
            MemoryValueKind.Text => Encoding.UTF8.GetBytes(value),
            MemoryValueKind.Hex => HexToBytes(value),
            _ => BitConverter.GetBytes((uint)ParseInteger(value))
        };
    }

    public static string ToDisplay(MemoryValueKind kind, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return "";
        }

        return kind switch
        {
            MemoryValueKind.Byte => bytes[0].ToString(CultureInfo.InvariantCulture),
            MemoryValueKind.UInt16 when bytes.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            MemoryValueKind.UInt32 when bytes.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            MemoryValueKind.UInt64 or MemoryValueKind.Pointer when bytes.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes).ToString(CultureInfo.InvariantCulture),
            MemoryValueKind.Float when bytes.Length >= 4 => BitConverter.ToSingle(bytes[..4]).ToString("G9", CultureInfo.InvariantCulture),
            MemoryValueKind.Double when bytes.Length >= 8 => BitConverter.ToDouble(bytes[..8]).ToString("G17", CultureInfo.InvariantCulture),
            MemoryValueKind.Text => Encoding.UTF8.GetString(bytes),
            _ => ToHex(bytes)
        };
    }

    public static string ToHex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    public static ulong ParseAddress(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return ulong.Parse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static bool Matches(
        MemoryValueKind kind,
        ScanCompareKind compare,
        ReadOnlySpan<byte> needle,
        ReadOnlySpan<byte> secondNeedle,
        ReadOnlySpan<byte> oldValue,
        ReadOnlySpan<byte> currentValue)
    {
        if (compare == ScanCompareKind.UnknownInitialValue)
        {
            return true;
        }

        if (compare == ScanCompareKind.ChangedValue)
        {
            return !currentValue.SequenceEqual(oldValue);
        }

        if (compare == ScanCompareKind.UnchangedValue)
        {
            return currentValue.SequenceEqual(oldValue);
        }

        if (kind is MemoryValueKind.Hex or MemoryValueKind.Text)
        {
            return compare == ScanCompareKind.ExactValue && currentValue.SequenceEqual(needle);
        }

        double current = ToNumber(kind, currentValue);
        double first = needle.IsEmpty ? 0 : ToNumber(kind, needle);
        double second = secondNeedle.IsEmpty ? 0 : ToNumber(kind, secondNeedle);
        double old = oldValue.IsEmpty ? current : ToNumber(kind, oldValue);

        return compare switch
        {
            ScanCompareKind.ExactValue => Math.Abs(current - first) < double.Epsilon,
            ScanCompareKind.FuzzyValue => Math.Abs(current - first) <= Math.Max(1.0, Math.Abs(first) * 0.01),
            ScanCompareKind.IncreasedValue => current > old,
            ScanCompareKind.IncreasedBy => Math.Abs((current - old) - first) < double.Epsilon,
            ScanCompareKind.DecreasedValue => current < old,
            ScanCompareKind.DecreasedBy => Math.Abs((old - current) - first) < double.Epsilon,
            ScanCompareKind.BiggerThan => current > first,
            ScanCompareKind.SmallerThan => current < first,
            ScanCompareKind.BetweenValue => current >= Math.Min(first, second) && current <= Math.Max(first, second),
            _ => false
        };
    }

    private static ulong ParseInteger(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.Parse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        return ulong.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    private static byte[] HexToBytes(string value)
    {
        string normalized = NormalizeHex(value);
        return Convert.FromHexString(normalized);
    }

    private static string NormalizeHex(string value)
    {
        string normalized = value.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "")
            .Replace("-", "")
            .Trim();
        return normalized.Length % 2 == 0 ? normalized : "0" + normalized;
    }

    private static double ToNumber(MemoryValueKind kind, ReadOnlySpan<byte> bytes)
    {
        return kind switch
        {
            MemoryValueKind.Byte when bytes.Length >= 1 => bytes[0],
            MemoryValueKind.UInt16 when bytes.Length >= 2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            MemoryValueKind.UInt32 when bytes.Length >= 4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            MemoryValueKind.UInt64 or MemoryValueKind.Pointer when bytes.Length >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            MemoryValueKind.Float when bytes.Length >= 4 => BitConverter.ToSingle(bytes[..4]),
            MemoryValueKind.Double when bytes.Length >= 8 => BitConverter.ToDouble(bytes[..8]),
            _ => 0
        };
    }
}
