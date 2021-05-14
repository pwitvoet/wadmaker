using SixLabors.ImageSharp.PixelFormats;

namespace WadMaker
{
    /// <summary>
    /// Settings for converting an image to a texture.
    /// </summary>
    struct TextureSettings
    {
        /// <summary>
        /// The dithering algorithm to apply when converting a source image to an 8-bit indexed texture.
        /// Defaults to <see cref="DitheringAlgorithm.FloydSteinberg"/> for normal textures,
        /// and to <see cref="DitheringAlgorithm.None"/> for animated textures (to prevent 'flickering' animations).
        /// </summary>
        public DitheringAlgorithm? DitheringAlgorithm { get; set; }

        /// <summary>
        /// When dithering is enabled, error diffusion is scaled by this factor (0 - 1).
        /// Setting this too high can result in dithering artifacts, setting it too low essentially disables dithering, resulting in banding.
        /// Defaults to 0.75.
        /// </summary>
        public float? DitherScale { get; set; }

        /// <summary>
        /// Pixels with an alpha value below this value will be ignored when the palette is created.
        /// For color-keyed textures (whose name must start with a '{'), they will be mapped to the last color in the palette.
        /// Defaults to 128 for transparent textures, and to 0 for all other textures.
        /// </summary>
        public int? TransparencyThreshold { get; set; }

        /// <summary>
        /// Water fog color (RGB) and intensity (A).
        /// The fog color defaults to the image's average color, and the intensity defaults to the inverse of the fog color's brightness.
        /// </summary>
        public Rgba32? WaterFogColor { get; set; }

        /// <summary>
        /// The command-line application that WadMaker will call to convert the current file.
        /// This also requires <see cref="ConverterArguments"/> to be set.
        /// WadMaker will use the output image to create a texture. The output image will be removed afterwards.
        /// </summary>
        public string Converter { get; set; }

        /// <summary>
        /// The arguments to pass to the converter application. These must include {input} and {output} markers, so WadMaker can pass the
        /// current file path and the location where the converter application must save the output image.
        /// </summary>
        public string ConverterArguments { get; set; }
    }
}
