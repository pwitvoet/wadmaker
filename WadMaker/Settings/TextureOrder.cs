namespace WadMaker.Settings
{
    static class TextureOrder
    {
        const string TextureOrderFilename = "texture_order.txt";


        public static bool IsTextureOrderFile(string path) => Path.GetFileName(path) == TextureOrderFilename;

        public static string[]? GetTextureOrder(string directory)
        {
            var textureOrderFilePath = Path.Combine(directory, TextureOrderFilename);
            if (!File.Exists(textureOrderFilePath))
                return null;

            return File.ReadAllLines(textureOrderFilePath)
                .Where(line => !string.IsNullOrEmpty(line) && !line.TrimStart().StartsWith("//"))
                .Select(line => line.Trim())
                .ToArray();
        }

        public static void SaveTextureOrder(string directory, IEnumerable<string> textureNames)
        {
            var textureOrderFilePath = Path.Combine(directory, TextureOrderFilename);
            File.WriteAllLines(textureOrderFilePath, textureNames.Prepend("// Texture order. Each line should contain one texture name."));
        }
    }
}
