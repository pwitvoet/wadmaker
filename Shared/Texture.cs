using SixLabors.ImageSharp.PixelFormats;

namespace Shared
{
    public enum TextureType : byte
    {
        SimpleTexture = 0x42,
        MipmapTexture = 0x43,
        Font = 0x46,
    }

    /// <summary>
    /// Half-Life textures use a palette of 256 colors.
    /// 
    /// Certain effects can be created by starting a texture name with one of the following characters:
    /// '{': transparent textures (the last color in the palette will become transparent)
    /// '!': water textures (the engine will apply a wave effect)
    /// '+': toggling and animating textures. The next character can either be a digit or a letter.
    ///      Subsequent digits and letters can be used to create animating sequences.
    ///      Toggling an entity that is covered with such a texture will switch between the number-based and the letter-based sequence.
    /// '-': random tiling textures.
    /// '~': light-emitting textures (this is only a convention, actual light emission depends on compile tool settings).
    /// </summary>
    public class Texture
    {
        public static Texture CreateMipmapTexture(
            string name,
            int width,
            int height,
            byte[]? imageData = null,
            IEnumerable<Rgba32>? palette = null,
            byte[]? mipmap1Data = null,
            byte[]? mipmap2Data = null,
            byte[]? mipmap3Data = null)
        {
            if (width < 1 || height < 1) throw new ArgumentException("Width and height must be greater than zero.");
            if (width % 16 != 0 || height % 16 != 0) throw new ArgumentException("Width and height must be multiples of 16.");
            if (imageData != null && imageData.Length != width * height) throw new ArgumentException("Image data must be 'width x height' bytes.", nameof(imageData));
            if (mipmap1Data != null && mipmap1Data.Length != width * height / 4) throw new ArgumentException("Mipmap 1 data must be 'width/2 x height/2' bytes.", nameof(mipmap1Data));
            if (mipmap2Data != null && mipmap2Data.Length != width * height / 16) throw new ArgumentException("Mipmap 2 data must be 'width/4 x height/4' bytes.", nameof(mipmap2Data));
            if (mipmap3Data != null && mipmap3Data.Length != width * height / 64) throw new ArgumentException("Mipmap 3 data must be 'width/8 x height/8' bytes.", nameof(mipmap3Data));
            if (palette != null && palette.Count() > 256) throw new ArgumentException("Palette must not contain more than 256 colors.", nameof(palette));

            return new Texture(TextureType.MipmapTexture, name, width, height, imageData ?? new byte[width * height], palette?.ToArray() ?? new Rgba32[256]) {
                Mipmap1Data = mipmap1Data ?? new byte[width * height / 4],
                Mipmap2Data = mipmap2Data ?? new byte[width * height / 16],
                Mipmap3Data = mipmap3Data ?? new byte[width * height / 64],
            };
        }

        public static Texture CreateSimpleTexture(
            string name,
            int width,
            int height,
            byte[]? imageData = null,
            IEnumerable<Rgba32>? palette = null)
        {
            if (width < 1 || height < 1) throw new ArgumentException("Width and height must be greater than zero.");
            if (imageData != null && imageData.Length != width * height) throw new ArgumentException("Image data must be 'width x height' bytes.", nameof(imageData));
            if (palette != null && palette.Count() > 256) throw new ArgumentException("Palette must not contain more than 256 colors.", nameof(palette));

            return new Texture(TextureType.SimpleTexture, name, width, height, imageData ?? new byte[width * height], palette?.ToArray() ?? new Rgba32[256]);
        }

        public static Texture CreateFont(
            string name,
            int width,
            int height,
            int rowCount,
            int rowHeight,
            IEnumerable<CharInfo> charInfos,
            byte[]? imageData = null,
            IEnumerable<Rgba32>? palette = null)
        {
            if (width != 256) throw new ArgumentException("Width must be 256.", nameof(width));
            if (height < 1) throw new ArgumentException("Height must be greater than zero.", nameof(height));
            if (rowCount < 1) throw new ArgumentException("Row count must be greater than zero.", nameof(rowCount));
            if (rowCount < 1) throw new ArgumentException("Row height must be greater than zero.", nameof(rowHeight));
            if (charInfos.Count() != 256) throw new ArgumentException("Exactly 256 char infos must be provided.", nameof(charInfos));
            if (imageData != null && imageData.Length != width * height) throw new ArgumentException("Image data must be 'width x height' bytes.", nameof(imageData));
            if (palette != null && palette.Count() > 256) throw new ArgumentException("Palette must not contain more than 256 colors.", nameof(palette));

            return new Texture(TextureType.Font, name, width, height, imageData ?? new byte[width * height], palette?.ToArray() ?? new Rgba32[256]) {
                RowCount = rowCount,
                RowHeight = rowHeight,
                CharInfos = charInfos?.ToArray() ?? new CharInfo[256],
            };
        }


        public TextureType Type { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }

        public byte[] ImageData { get; }

        // Texture and Decal only:
        public byte[]? Mipmap1Data { get; private set; }
        public byte[]? Mipmap2Data { get; private set; }
        public byte[]? Mipmap3Data { get; private set; }

        // Font only:
        public int RowCount { get; private set;  }
        public int RowHeight { get; private set;  }
        public CharInfo[]? CharInfos { get; private set;  }

        // Texture and Font only:
        public Rgba32[] Palette { get; }


        private Texture(TextureType type, string name, int width, int height, byte[] imageData, Rgba32[] palette)
        {
            Type = type;
            Name = name;
            Width = width;
            Height = height;
            ImageData = imageData;
            Palette = palette;
        }

        public byte[]? GetImageData(int mipmapLevel = 0)
        {
            switch (mipmapLevel)
            {
                default:
                case 0: return ImageData;
                case 1: return Mipmap1Data;
                case 2: return Mipmap2Data;
                case 3: return Mipmap3Data;
            }
        }
    }

    public struct CharInfo
    {
        public int StartOffset;
        public int CharWidth;


        public CharInfo(int startOffset, int charWidth)
        {
            StartOffset = startOffset;
            CharWidth = charWidth;
        }
    }
}
