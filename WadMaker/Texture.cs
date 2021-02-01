using System.Drawing;

namespace WadMaker
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
        public TextureType Type { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] ImageData { get; set; }

        // Texture and Decal only:
        public byte[] Mipmap1Data { get; set; }
        public byte[] Mipmap2Data { get; set; }
        public byte[] Mipmap3Data { get; set; }

        // Font only:
        public int RowCount { get; set; }
        public int RowHeight { get; set; }
        public CharInfo[] CharInfos { get; set; }

        // Texture and Font only:
        public int ColorsUsed { get; set; }
        public Color[] Palette { get; set; }
    }

    public struct CharInfo
    {
        public int StartOffset;
        public int CharWidth;
    }
}
