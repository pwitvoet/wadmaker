using Shared;
using System.Diagnostics;

namespace WadMaker
{
    public static class TextureEmbedding
    {
        public static void RemoveEmbeddedTextures(string bspFilePath, string outputFilePath, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            if (Path.GetExtension(bspFilePath).ToLowerInvariant() != ".bsp")
                throw new InvalidUsageException("Removing embedded textures requires a .bsp file.");

            logger.Log($"Removing embedded textures from '{bspFilePath}' and saving the result to '{outputFilePath}'.");

            var removedTextureCount = Bsp.RemoveEmbeddedTextures(bspFilePath, outputFilePath);

            logger.Log($"Removed {removedTextureCount} embedded textures from '{bspFilePath}' in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }
    }
}
