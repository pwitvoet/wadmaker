﻿using Shared;
using Shared.Sprites;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SpriteMaker
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
    struct SpriteSettings
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
    }
}
