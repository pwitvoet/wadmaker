using Shared.FileSystem;

namespace SpriteMaker.Settings
{
    class SpriteSourceFileInfo : Shared.FileSystem.FileInfo
    {
        public SpriteSettings Settings { get; }


        public SpriteSourceFileInfo(string path, int fileSize, FileHash fileHash, DateTimeOffset lastModified, SpriteSettings settings)
            : base(path, fileSize, fileHash, lastModified)
        {
            Settings = settings;
        }
    }
}
