
namespace WadMaker
{
    class TextureSettings
    {
        /// <summary>
        /// The number of cuts after which the median-cut color quantization algorithm will switch to a different strategy.
        /// For the first few cuts, bounding boxes are selected on color count alone. After that, volume is taken into account,
        /// to give rarer but still notable colors more chance.
        /// Defaults to 32.
        /// </summary>
        public int? QuantizationVolumeSelectionTreshold { get; set; }

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
        public int? TransparencyTreshold { get; set; }

        /// <summary>
        /// The maximum amount of error that can be accumulated per channel.
        /// This only affects Floyd-Steinberg dithering. Defaults to 255.
        /// </summary>
        public int? MaxErrorDiffusion { get; set; }
    }
}
