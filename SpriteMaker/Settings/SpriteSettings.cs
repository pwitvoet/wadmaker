using Shared;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SpriteMaker.Settings
{
    enum IndexAlphaTransparencySource
    {
        AlphaChannel,
        Grayscale,
    }

    /// <summary>
    /// Settings for converting an image or group of images to a sprite.
    /// The type and texture format settings apply to the sprite itself.
    /// All other settings can be applied to individual frame images.
    /// </summary>
    class SpriteSettings : IEquatable<SpriteSettings>
    {
        /// <summary>
        /// When true, the source image(s) are ignored - as if they don't exist.
        /// </summary>
        public bool? Ignore { get; set; }


        /// <summary>
        /// The sprite's type. Defaults to <see cref="SpriteType.Parallel"/>.
        /// This can be set by adding a specific suffix to a source image filename:
        /// <list type="bullet">
        /// <item>".pu": <see cref="SpriteType.ParallelUpright"/></item>
        /// <item>".u": <see cref="SpriteType.Upright"/></item>
        /// <item>".p": <see cref="SpriteType.Parallel"/> (default, suffix can be omitted)</item>
        /// <item>".o": <see cref="SpriteType.Oriented"/></item>
        /// <item>".po": <see cref="SpriteType.ParallelOriented"/></item>
        /// </list>
        /// Filename suffixes take priority over spritemaker.config settings.
        /// For multi-frame sprites, only the settings for the first frame image will apply.
        /// </summary>
        public SpriteType? SpriteType { get; set; }

        /// <summary>
        /// The sprite's texture format. Defaults to <see cref="SpriteTextureFormat.Additive"/>.
        /// This can be set by adding a specific suffix to a source image filename:
        /// <list type="bullet">
        /// <item>".n": <see cref="SpriteTextureFormat.Normal"/></item>
        /// <item>".a": <see cref="SpriteTextureFormat.Additive"/> (default, suffix can be omitted)</item>
        /// <item>".ia": <see cref="SpriteTextureFormat.IndexAlpha"/></item>
        /// <item>".at": <see cref="SpriteTextureFormat.AlphaTest"/></item>
        /// </list>
        /// Filename suffixes take priority over spritemaker.config settings.
        /// For multi-frame sprites, only the settings for the first frame image will apply.
        /// </summary>
        public SpriteTextureFormat? SpriteTextureFormat { get; set; }

        /// <summary>
        /// Multiple images with the same sprite name will be combined into an animated sprite.
        /// Their frame numbers determine how they are ordered.
        /// Frame numbers do not need to be consecutive.
        /// </summary>
        public int? FrameNumber { get; set; }

        /// <summary>
        /// If this is set, the image is cut up into tiles, and each tile is used as a separate frame.
        /// </summary>
        public Size? SpritesheetTileSize { get; set; }

        /// <summary>
        /// The offset of this frame relative to the center of the image.
        /// Defaults to (0, 0), which centers the frame at the sprite's position.
        /// Positive X values move the frame to the right, positive Y values move the frame upwards.
        /// </summary>
        public Point? FrameOffset { get; set; }


        /// <summary>
        /// The dithering algorithm to apply when converting a source image to an 8-bit indexed sprite.
        /// Defaults to <see cref="DitheringAlgorithm.FloydSteinberg"/> for normal sprites,
        /// and to <see cref="DitheringAlgorithm.None"/> for animated sprites (to prevent 'flickering' animations).
        /// </summary>
        public DitheringAlgorithm? DitheringAlgorithm { get; set; }

        /// <summary>
        /// When dithering is enabled, error diffusion is scaled by this factor (0 - 1).
        /// Setting this too high can result in dithering artifacts, setting it too low essentially disables dithering, resulting in banding.
        /// Defaults to 0.75.
        /// </summary>
        public float? DitherScale { get; set; }


        /// <summary>
        /// Pixels with an alpha value below this value will be ignored when the palette is created, and they will be mapped to the last color in the palette.
        /// Defaults to 128.
        /// This setting only applies to alpha-test sprites.
        /// </summary>
        public int? AlphaTestTransparencyThreshold { get; set; }

        /// <summary>
        /// Pixels with this color will be ignored when the palette is created, and they will be mapped to the last color in the palette.
        /// This is not used by default.
        /// This setting only applies to alpha-test sprites.
        /// </summary>
        public Rgba32? AlphaTestTransparencyColor { get; set; }


        /// <summary>
        /// The channel that determines the transparency for index-alpha sprites.
        /// Defaults to <see cref="IndexAlphaTransparencySource.AlphaChannel"/>.
        /// This setting only applies to index-alpha sprites.
        /// </summary>
        public IndexAlphaTransparencySource? IndexAlphaTransparencySource { get; set; }

        /// <summary>
        /// Index-alpha sprite color (RGB).
        /// The sprite's color defaults to the image's average color.
        /// This setting only applies to index-alpha sprites.
        /// </summary>
        public Rgba32? IndexAlphaColor { get; set; }


        /// <summary>
        /// The command-line application that SpriteMaker will call to convert the current file.
        /// This also requires <see cref="ConverterArguments"/> to be set.
        /// SpriteMaker will use the output image to create a sprite. The output image will be removed afterwards.
        /// </summary>
        public string? Converter { get; set; }

        /// <summary>
        /// The arguments to pass to the converter application. These must include {input} and {output} markers, so SpriteMaker can pass the
        /// current file path and the location where the converter application must save the output image.
        /// </summary>
        public string? ConverterArguments { get; set; }


        /// <summary>
        /// Updates the current settings with the given settings.
        /// </summary>
        public void OverrideWith(SpriteSettings overrideSettings)
        {
            if (overrideSettings.Ignore != null) Ignore = overrideSettings.Ignore;
            if (overrideSettings.SpriteType != null) SpriteType = overrideSettings.SpriteType;
            if (overrideSettings.SpriteTextureFormat != null) SpriteTextureFormat = overrideSettings.SpriteTextureFormat;
            if (overrideSettings.FrameNumber != null) FrameNumber = overrideSettings.FrameNumber;
            if (overrideSettings.SpritesheetTileSize != null) SpritesheetTileSize = overrideSettings.SpritesheetTileSize;
            if (overrideSettings.FrameOffset != null) FrameOffset = overrideSettings.FrameOffset;
            if (overrideSettings.DitheringAlgorithm != null) DitheringAlgorithm = overrideSettings.DitheringAlgorithm;
            if (overrideSettings.DitherScale != null) DitherScale = overrideSettings.DitherScale;
            if (overrideSettings.AlphaTestTransparencyThreshold != null) AlphaTestTransparencyThreshold = overrideSettings.AlphaTestTransparencyThreshold;
            if (overrideSettings.AlphaTestTransparencyColor != null) AlphaTestTransparencyColor = overrideSettings.AlphaTestTransparencyColor;
            if (overrideSettings.IndexAlphaTransparencySource != null) IndexAlphaTransparencySource = overrideSettings.IndexAlphaTransparencySource;
            if (overrideSettings.IndexAlphaColor != null) IndexAlphaColor = overrideSettings.IndexAlphaColor;
            if (overrideSettings.Converter != null) Converter = overrideSettings.Converter;
            if (overrideSettings.ConverterArguments != null) ConverterArguments = overrideSettings.ConverterArguments;
        }


        public bool Equals(SpriteSettings? other)
        {
            return other is not null &&
                Ignore == other.Ignore &&
                SpriteType == other.SpriteType &&
                SpriteTextureFormat == other.SpriteTextureFormat &&
                FrameNumber == other.FrameNumber &&
                SpritesheetTileSize == other.SpritesheetTileSize &&
                FrameOffset == other.FrameOffset &&
                DitheringAlgorithm == other.DitheringAlgorithm &&
                DitherScale == other.DitherScale &&
                AlphaTestTransparencyThreshold == other.AlphaTestTransparencyThreshold &&
                AlphaTestTransparencyColor == other.AlphaTestTransparencyColor &&
                IndexAlphaTransparencySource == other.IndexAlphaTransparencySource &&
                IndexAlphaColor == other.IndexAlphaColor &&
                Converter == other.Converter &&
                ConverterArguments == other.ConverterArguments;
        }

        public override bool Equals(object? obj) => obj is SpriteSettings other && Equals(other);

        public override int GetHashCode() => 0; // Just do an equality check.

        public static bool operator ==(SpriteSettings? left, SpriteSettings? right) => left?.Equals(right) ?? right is null;
        public static bool operator !=(SpriteSettings? left, SpriteSettings? right) => !(left?.Equals(right) ?? right is null);
    }
}
