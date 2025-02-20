using System.Text.Json;
using FileInfo = Shared.FileSystem.FileInfo;

namespace WadMaker.Settings
{
    class WadMakingHistory
    {
        private static JsonSerializerOptions SerializerOptions { get; }

        static WadMakingHistory()
        {
            SerializerOptions = new JsonSerializerOptions();
            SerializerOptions.Converters.Add(new WadMakingHistoryJsonSerializer());
        }


        const string HistoryFilename = "wadmaker.dat";


        public FileInfo OutputFile { get; }
        public Dictionary<string, TextureSourceFileInfo[]> TextureInputs { get; }


        public WadMakingHistory(FileInfo outputFile, IDictionary<string, TextureSourceFileInfo[]> textureInputs)
        {
            OutputFile = outputFile;
            TextureInputs = textureInputs.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        public static WadMakingHistory? Load(string folder)
        {
            try
            {
                var historyFilePath = Path.Combine(folder, HistoryFilename);
                if (!File.Exists(historyFilePath))
                    return null;

                var json = File.ReadAllText(historyFilePath);
                return JsonSerializer.Deserialize<WadMakingHistory>(json, SerializerOptions);
            }
            catch
            {
                // Error reading file? Just ignore - history only matters when doing incremental updates, and we can always fall back to doing a full rebuild:
                return null;
            }
        }

        public static bool IsHistoryFile(string path) => Path.GetFileName(path) == HistoryFilename;


        public void Save(string folder)
        {
            var historyFilePath = Path.Combine(folder, HistoryFilename);
            File.WriteAllText(historyFilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
    }
}
