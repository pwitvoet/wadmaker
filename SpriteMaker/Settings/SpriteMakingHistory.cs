using System.Text.Json;
using FileInfo = Shared.FileSystem.FileInfo;

namespace SpriteMaker.Settings
{
    class SpriteMakingHistory
    {
        public class SpriteHistory
        {
            public FileInfo OutputFile { get; }
            public SpriteSourceFileInfo[] InputFiles { get; }

            public SpriteHistory(FileInfo outputFile, SpriteSourceFileInfo[] inputFiles)
            {
                OutputFile = outputFile;
                InputFiles = inputFiles;
            }
        }


        private static JsonSerializerOptions SerializerOptions { get; }

        static SpriteMakingHistory()
        {
            SerializerOptions = new JsonSerializerOptions();
            SerializerOptions.Converters.Add(new SpriteMakingHistoryJsonSerializer());
        }


        const string HistoryFilename = "spritemaker.dat";


        public Dictionary<string, SpriteHistory> Sprites { get; }
        public string[] SubDirectoryNames { get; }


        public SpriteMakingHistory(IDictionary<string, SpriteHistory> sprites, IEnumerable<string> subDirectoryNames)
        {
            Sprites = sprites.ToDictionary(kv => kv.Key, kv => kv.Value);
            SubDirectoryNames = subDirectoryNames.ToArray();
        }

        public static SpriteMakingHistory? Load(string folder)
        {
            try
            {
                var historyFilePath = Path.Combine(folder, HistoryFilename);
                if (!File.Exists(historyFilePath))
                    return null;

                var json = File.ReadAllText(historyFilePath);
                return JsonSerializer.Deserialize<SpriteMakingHistory>(json, SerializerOptions);
            }
            catch
            {
                // Error reading file? Just ignore - history only matters when doing incremental updates, and we can always fall back to doing a full rebuild!
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
