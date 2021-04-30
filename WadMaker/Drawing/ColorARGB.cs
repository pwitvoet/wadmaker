using System;
using System.Runtime.InteropServices;

namespace WadMaker.Drawing
{
    [StructLayout(LayoutKind.Explicit)]
    public struct ColorARGB : IEquatable<ColorARGB>
    {
        // [BBBBBBBB] [GGGGGGGG] [RRRRRRRR] [AAAAAAAA]
        [FieldOffset(3)] public byte A;
        [FieldOffset(2)] public byte R;
        [FieldOffset(1)] public byte G;
        [FieldOffset(0)] public byte B;

        [FieldOffset(0)] public int Value;

        public ColorARGB(byte a, byte r, byte g, byte b)
            : this()
        {
            // TODO: What would be faster - assigning each field, or doing bitwise combination and assigning to Value???
            A = a;
            R = r;
            G = g;
            B = b;
        }

        public ColorARGB(byte r, byte g, byte b)
            : this(255, r, g, b)
        {
        }

        public ColorARGB(int value)
            : this()
        {
            Value = value;
        }


        // TODO: This requires further investigation!
        public float GetBrightness() => (R * 0.21f + G * 0.72f + B * 0.07f) / 255;


        public override bool Equals(object obj) => obj is ColorARGB other && Value == other.Value;

        public override int GetHashCode() => Value;

        public bool Equals(ColorARGB other) => Value == other.Value;
    }
}
