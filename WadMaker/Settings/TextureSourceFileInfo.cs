using Shared.FileSystem;

namespace WadMaker.Settings
{
    class TextureSourceFileInfo : Shared.FileSystem.FileInfo
    {
        public TextureSettings Settings { get; }


        public TextureSourceFileInfo(string path, int fileSize, FileHash fileHash, DateTimeOffset lastModified, TextureSettings settings)
            : base(path, fileSize, fileHash, lastModified)
        {
            Settings = settings;
        }
    }
}
