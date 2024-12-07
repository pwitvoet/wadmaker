using Shared;
using System.Diagnostics;

namespace WadMaker
{
    public static class TextureEmbedding
    {
        public static void EmbedTextures(string wadFilePath, string bspFilePath, string outputFilePath, Logger logger)
        {
            var stopwatch = Stopwatch.StartNew();

            if (Path.GetExtension(wadFilePath).ToLowerInvariant() != ".wad")
                throw new InvalidUsageException($"Updating embedded textures requires a source .wad file.");

            if (Path.GetExtension(bspFilePath).ToLowerInvariant() != ".bsp")
                throw new InvalidUsageException($"Updating embedded textures requires a target .bsp file.");

            logger.Log($"Updating embedded textures in '{bspFilePath}', using textures from '{wadFilePath}', and saving the result to '{outputFilePath}'.");

            var wadFile = Wad.Load(wadFilePath, (index, name, exception) => logger.Log($"- Failed to load texture #{index} ('{name}'): {exception.GetType().Name}: '{exception.Message}'."));
            var embeddedTextureCount = Bsp.EmbedTextures(wadFile, bspFilePath, outputFilePath);

            logger.Log($"Embedded {embeddedTextureCount} textures in '{bspFilePath}', using textures from '{wadFilePath}', in {stopwatch.Elapsed.TotalSeconds:0.000} seconds.");
        }

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
