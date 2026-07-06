using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace PS5MemoryPeeker;

public static class CheatExportService
{
    private static readonly byte[] Mc4Key = Encoding.ASCII.GetBytes("304c6528f659c766110239a51cl5dd9c");
    private static readonly byte[] Mc4Iv = Encoding.ASCII.GetBytes("u@}kzW2u[u(8DWar");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task ExportAsync(string path, IEnumerable<CheatRow> cheats, ProcessItem? process, CancellationToken token)
    {
        List<CheatRow> activeCheats = cheats.Where(c => c.IsActive).ToList();
        string extension = Path.GetExtension(path).ToLowerInvariant();
        switch (extension)
        {
            case ".json":
                await File.WriteAllTextAsync(path, BuildJson(activeCheats, process), Encoding.UTF8, token);
                break;
            case ".shn":
                await File.WriteAllTextAsync(path, BuildShn(activeCheats, process), Encoding.Unicode, token);
                break;
            case ".mc4":
                await File.WriteAllTextAsync(path, EncryptMc4(BuildShn(activeCheats, process)), Encoding.ASCII, token);
                break;
            default:
                throw new NotSupportedException("Export supports .json, .shn and .mc4 only.");
        }
    }

    private static string BuildJson(IReadOnlyList<CheatRow> cheats, ProcessItem? process)
    {
        object payload = new
        {
            name = "PS5 MemoryPeeker Export",
            id = ExportId(process),
            version = "UNKNOWN",
            process = "eboot.bin",
            mods = cheats.Select(cheat => new
            {
                name = CheatName(cheat),
                type = "checkbox",
                memory = new[]
                {
                    new
                    {
                        offset = ToHex(cheat.SectionOffset),
                        on = ValueOn(cheat, compact: true),
                        off = ValueOff(cheat, compact: true),
                        absolute = cheat.SectionStart == 0,
                        address = $"0x{cheat.Address:X}",
                        section = cheat.Section
                    }
                }
            }).ToList(),
            credits = new[] { "PS5-MemoryPeeker" },
            source = new
            {
                app = "PS5-MemoryPeeker",
                export = "runtime-address",
                note = "Cheats exported from scan results include section offsets when available. No pointer chains are generated."
            }
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildShn(IReadOnlyList<CheatRow> cheats, ProcessItem? process)
    {
        XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
        XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";

        XElement trainer = new("Trainer",
            new XAttribute(XNamespace.Xmlns + "xsd", xsd),
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute("Game", "PS5 MemoryPeeker Export"),
            new XAttribute("Moder", "PS5-MemoryPeeker"),
            new XAttribute("Cusa", ExportId(process)),
            new XAttribute("Version", "UNKNOWN"),
            new XAttribute("Process", "eboot.bin"),
            new XElement("Genres", new XAttribute("Name", "Memory")),
            new XElement("Items"));

        foreach (CheatRow cheat in cheats)
        {
            trainer.Add(new XElement("Cheat",
                new XAttribute("Text", CheatName(cheat)),
                new XElement("Cheatline",
                    new XElement("Absolute", cheat.SectionStart == 0 ? "True" : "False"),
                    new XElement("Section", "0"),
                    new XElement("Offset", ToHex(cheat.SectionOffset)),
                    new XElement("ValueOn", ValueOn(cheat, compact: false)),
                    new XElement("ValueOff", ValueOff(cheat, compact: false)))));
        }

        XDocument document = new(new XDeclaration("1.0", "utf-16", null), trainer);
        return document.ToString(SaveOptions.None);
    }

    private static string EncryptMc4(string shn)
    {
        byte[] plain = Encoding.UTF8.GetBytes(shn);
        int padding = 16 - plain.Length % 16;
        if (padding == 0)
        {
            padding = 16;
        }

        byte[] padded = new byte[plain.Length + padding];
        Buffer.BlockCopy(plain, 0, padded, 0, plain.Length);
        Array.Fill(padded, (byte)padding, plain.Length, padding);

        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = Mc4Key;
        aes.IV = Mc4Iv;

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        byte[] encrypted = encryptor.TransformFinalBlock(padded, 0, padded.Length);
        return Convert.ToBase64String(encrypted);
    }

    private static string ValueOn(CheatRow cheat, bool compact)
    {
        try
        {
            return FormatBytes(MemoryValueCodec.ToBytes(cheat.Type, cheat.Value), compact);
        }
        catch
        {
            return NormalizeHex(cheat.Value, compact);
        }
    }

    private static string ValueOff(CheatRow cheat, bool compact)
    {
        if (!string.IsNullOrWhiteSpace(cheat.OriginalHex))
        {
            return NormalizeHex(cheat.OriginalHex, compact);
        }

        int size = Math.Max(1, MemoryValueCodec.GetSize(cheat.Type, cheat.Value));
        return FormatBytes(new byte[size], compact);
    }

    private static string FormatBytes(byte[] bytes, bool compact)
    {
        string hex = Convert.ToHexString(bytes);
        return compact ? hex : string.Join("-", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    private static string NormalizeHex(string value, bool compact)
    {
        string hex = value.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-", "")
            .Replace(" ", "")
            .Trim()
            .ToUpperInvariant();
        if (hex.Length % 2 != 0)
        {
            hex = "0" + hex;
        }

        return compact ? hex : string.Join("-", Enumerable.Range(0, hex.Length / 2).Select(i => hex.Substring(i * 2, 2)));
    }

    private static string CheatName(CheatRow cheat)
    {
        if (!string.IsNullOrWhiteSpace(cheat.Description))
        {
            return cheat.Description;
        }

        return $"{cheat.TypeText} @ 0x{cheat.Address:X}";
    }

    private static string ExportId(ProcessItem? process)
    {
        string id = process?.TitleId ?? "";
        return string.IsNullOrWhiteSpace(id) ? "UNKNOWN" : id.ToUpperInvariant();
    }

    private static string ToHex(ulong value) => value.ToString("X", CultureInfo.InvariantCulture);
}
