using System.Drawing;

namespace WadMaker
{
    /// <summary>
    /// Settings for converting an image to a texture.
    /// </summary>
    struct TextureSettings
    {
        /// <summary>
        /// The number of cuts after which the median-cut color quantization algorithm will switch to a different strategy.
        /// For the first few cuts, bounding boxes are selected on color count alone. After that, volume is taken into account,
        /// to give rarer but still notable colors more chance.
        /// Defaults to 32.
        /// </summary>
        public int? QuantizationVolumeSelectionThreshold { get; set; }

        /// <summary>
        /// The dithering algorithm to apply when converting a source image to an 8-bit indexed texture.
        /// Defaults to <see cref="DitheringAlgorithm.FloydSteinberg"/> for normal textures,
        /// and to <see cref="DitheringAlgorithm.None"/> for animated textures (to prevent 'flickering' animations).
        /// </summary>
        public DitheringAlgorithm? DitheringAlgorithm { get; set; }

        /// <summary>
        /// Pixels with an alpha value below this value will be ignored when the palette is created.
        /// For color-keyed textures (whose name must start with a '{'), they will be mapped to the last color in the palette.
        /// Defaults to 128 for transparent textures, and to 0 for all other textures.
        /// </summary>
        public int? TransparencyThreshold { get; set; }

        /// <summary>
        /// The maximum amount of error that can be accumulated per channel.
        /// This only affects Floyd-Steinberg dithering. Defaults to 255.
        /// </summary>
        public int? MaxErrorDiffusion { get; set; }

        /// <summary>
        /// Water fog color.
        /// Defaults to the image's average color.
        /// </summary>
        public Color? WaterFogColor { get; set; }

        /// <summary>
        /// Water fog intensity. Ranges from 0 (low intensity, increased view distance) to 255 (high intensity, reduced view distance).
        /// Defaults to the inverse of the water fog color's brightness.
        /// </summary>
        public int? WaterFogIntensity { get; set; }
    }
}
