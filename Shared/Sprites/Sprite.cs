using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared.Sprites
{
    public enum SpriteType : uint
    {
        ParallelUpright = 0,
        Upright = 1,
        Parallel = 2,
        Oriented = 3,
        ParallelOriented = 4,
    }

    public enum SpriteTextureFormat : uint
    {
        Normal = 0,
        Additive = 1,
        IndexAlpha = 2,
        AlphaTest = 3,
    }

    public enum SpriteSynchronization : uint
    {
        Synchronized = 0,
        Random = 1,
    }

    public class Sprite
    {
        public static Sprite CreateSprite(SpriteType type, SpriteTextureFormat textureFormat, int maxWidth, int maxHeight, Rgba32[] palette)
        {
            if (maxWidth < 1 || maxHeight < 1) throw new ArgumentException("Width and height must greater than zero.");
            if (palette?.Count() > 256) throw new ArgumentException("Palette must not contain more than 256 colors.", nameof(palette));

            return new Sprite(
                type,
                textureFormat,
                (float)Math.Sqrt(maxWidth * maxWidth + maxHeight * maxHeight) / 2,
                (uint)maxWidth,
                (uint)maxHeight,
                0,
                SpriteSynchronization.Random,
                palette);
        }


        public SpriteType Type { get; set; }
        public SpriteTextureFormat TextureFormat { get; set; }
        public float BoundingRadius { get; }
        public uint MaximumWidth { get; }
        public uint MaximumHeight { get; }
        public float BeamLength { get; }                            // Unused?
        public SpriteSynchronization Synchronization { get; set; }  // Unused?
        public Rgba32[] Palette { get; }
        public List<Frame> Frames { get; } = new List<Frame>();


        public void Save(string path)
        {
            using (var file = File.Create(path))
                Save(file);
        }

        public void Save(Stream stream)
        {
            stream.Write("IDSP");
            stream.Write((uint)2);  // version
            stream.Write((uint)Type);
            stream.Write((uint)TextureFormat);
            stream.Write(BoundingRadius);
            stream.Write(MaximumWidth);
            stream.Write(MaximumHeight);
            stream.Write((uint)Frames.Count);
            stream.Write(BeamLength);
            stream.Write((uint)Synchronization);

            stream.Write((ushort)Palette.Length);
            foreach (var color in Palette)
                stream.Write(color);

            foreach (var frame in Frames)
                WriteFrame(stream, frame);
        }


        public static Sprite Load(string path)
        {
            using (var file = File.OpenRead(path))
                return Load(file);
        }

        public static Sprite Load(Stream stream)
        {
            var fileSignature = stream.ReadString(4);
            if (fileSignature != "IDSP")
                throw new InvalidDataException($"Expected file to start with 'IDSP' but found '{fileSignature}'.");

            var version = stream.ReadUint();
            var type = (SpriteType)stream.ReadUint();
            var textureFormat = (SpriteTextureFormat)stream.ReadUint();
            var boundingRadius = stream.ReadFloat();
            var maximumWidth = stream.ReadUint();
            var maximumHeight = stream.ReadUint();
            var frameCount = stream.ReadUint();
            var beamLength = stream.ReadFloat();
            var synchronization = (SpriteSynchronization)stream.ReadUint();

            var paletteSize = stream.ReadUshort();
            var palette = Enumerable.Range(0, paletteSize)
                .Select(i => stream.ReadColor())
                .ToArray();

            var frames = Enumerable.Range(0, (int)frameCount)
                .Select(i => ReadFrame(stream))
                .ToArray();

            var sprite = new Sprite(type, textureFormat, boundingRadius, maximumWidth, maximumHeight, beamLength, synchronization, palette);
            sprite.Frames.AddRange(frames);
            return sprite;
        }


        private static void WriteFrame(Stream stream, Frame frame)
        {
            stream.Write((uint)frame.Type);
            stream.Write(frame.FrameOriginX);
            stream.Write(frame.FrameOriginY);
            stream.Write(frame.FrameWidth);
            stream.Write(frame.FrameHeight);
            stream.Write(frame.ImageData);
        }

        private static Frame ReadFrame(Stream stream)
        {
            var frame = new Frame();

            frame.Type = (FrameType)stream.ReadUint();
            frame.FrameOriginX = stream.ReadInt();
            frame.FrameOriginY = stream.ReadInt();
            frame.FrameWidth = stream.ReadUint();
            frame.FrameHeight = stream.ReadUint();
            frame.ImageData = stream.ReadBytes((int)(frame.FrameWidth * frame.FrameHeight));

            return frame;
        }


        private Sprite(
            SpriteType type,
            SpriteTextureFormat textureFormat,
            float boundingRadius,
            uint maximumWidth,
            uint maximumHeight,
            float beamLength,
            SpriteSynchronization synchronization,
            Rgba32[] palette)
        {
            Type = type;
            TextureFormat = textureFormat;
            BoundingRadius = boundingRadius;
            MaximumWidth = maximumWidth;
            MaximumHeight = maximumHeight;
            BeamLength = beamLength;
            Synchronization = synchronization;
            Palette = palette;
        }
    }

    public enum FrameType : uint
    {
        Single = 0,
        Group = 1,  // Does not seem to be supported by Half-Life
    }

    public class Frame
    {
        public FrameType Type { get; set; }
        public int FrameOriginX { get; set; }
        public int FrameOriginY { get; set; }
        public uint FrameWidth { get; set; }
        public uint FrameHeight { get; set; }
        public byte[] ImageData { get; set; }   // frame width * frame height, indexing into palette
    }
}
