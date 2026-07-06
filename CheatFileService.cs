using System.Text.Json;
using System.IO;

namespace PS5MemoryPeeker;

public static class CheatFileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task SaveAsync(string path, IEnumerable<CheatRow> cheats, CancellationToken cancellationToken)
    {
        CheatFile file = new()
        {
            App = "PS5-MemoryPeeker",
            Version = 1,
            Cheats = cheats.ToList()
        };

        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, file, Options, cancellationToken);
    }

    public static async Task<IReadOnlyList<CheatRow>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        CheatFile? file = await JsonSerializer.DeserializeAsync<CheatFile>(stream, Options, cancellationToken);
        return file?.Cheats ?? [];
    }

    private sealed class CheatFile
    {
        public string App { get; set; } = "";
        public int Version { get; set; }
        public List<CheatRow> Cheats { get; set; } = [];
    }
}
