using System.Text.RegularExpressions;

namespace Shared
{
    public static class TextureName
    {
        private static Regex AnimatedTextureNameRegex = new Regex(@"^\+[0-9A-J]", RegexOptions.IgnoreCase);


        public static bool IsWater(string name) => name.StartsWith('!');

        public static bool IsTransparent(string name) => name.StartsWith('{');

        public static bool IsAnimated(string name) => AnimatedTextureNameRegex.IsMatch(name);

        public static bool IsFullbright(string name) => name.StartsWith('~') || IsAnimated(name) && name.Length >= 3 && name[2] == '~';
    }
}
