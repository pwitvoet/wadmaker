using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp;
using Shared.FileFormats.Pdn;
using System.Formats.Nrbf;

namespace Shared.FileFormats
{
    public class PdnReader : IImageReader
    {
        public string[] SupportedExtensions { get; } = new[] { "pdn" };


        public Image<Rgba32> ReadImage(string path)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var pdnDocument = ReadPdnDocument(file);
                return PdnDrawer.CreateCompositeImage(pdnDocument);
            }
        }


        private static PdnDocument ReadPdnDocument(Stream stream)
        {
            var signature = stream.ReadString(4);
            if (signature != "PDN3")
                throw new InvalidDataException($"Invalid file signature: '{signature}'.");

            var xmlLength = stream.ReadInt24();
            stream.Position += xmlLength;

            var version = stream.ReadShort();
            if (version != 256)
                throw new NotSupportedException($"File version {version} is not supported.");

            var rootRecord = NrbfDecoder.Decode(stream, out var recordMap, leaveOpen: true);
            if (rootRecord is not ClassRecord classRecord)
                throw new InvalidDataException($"Invalid file structure.");

            var readerContext = new PdnReaderContext(new PdnMemoryBlockDeserializer());
            var pdnDocument = ReadPdnDocument(classRecord, readerContext);

            readerContext.MemoryBlockDeserializer.Deserialize(stream);
            return pdnDocument;
        }

        private static PdnDocument ReadPdnDocument(ClassRecord record, PdnReaderContext context)
        {
            var width = record.GetInt32("width");
            var height = record.GetInt32("height");
            var layersRecord = record.GetSerializationRecord("layers") as ClassRecord;
            var savedWithRecord = record.GetSerializationRecord("savedWith") as ClassRecord;
            var userMetadataRecord = record.GetSerializationRecord("userMetadataItems") as ArrayRecord;

            if (layersRecord is null || savedWithRecord is null || userMetadataRecord is null)
                throw new InvalidDataException("Invalid document structure.");

            return new PdnDocument(width, height, ReadPdnLayersList(layersRecord, context), ReadVersion(savedWithRecord), ReadKeyValuePairs(userMetadataRecord));
        }

        private static PdnLayer[] ReadPdnLayersList(ClassRecord record, PdnReaderContext context)
        {
            var array = record.GetSerializationRecord("ArrayList+_items") as ArrayRecord;
            if (array is null)
                throw new InvalidDataException("Invalid layer list structure.");

            var items = array.GetArray(typeof(object[])) as object[];
            if (items is null)
                throw new InvalidDataException("Invalid layer list structure.");

            return items
                .OfType<ClassRecord>()
                .Select(itemRecord => ReadPdnLayer(itemRecord, context))
                .ToArray();
        }

        private static PdnLayer ReadPdnLayer(ClassRecord record, PdnReaderContext context)
        {
            var width = record.GetInt32("Layer+width");
            var height = record.GetInt32("Layer+height");
            var surfaceRecord = record.GetSerializationRecord("surface") as ClassRecord;
            var layerPropertiesRecord = record.GetSerializationRecord("Layer+properties") as ClassRecord;

            if (surfaceRecord is null || layerPropertiesRecord is null)
                throw new InvalidDataException("Invalid layer structure.");

            return new PdnLayer(width, height, ReadSurface(surfaceRecord, context), ReadLayerProperties(layerPropertiesRecord));
        }

        private static PdnSurface ReadSurface(ClassRecord record, PdnReaderContext context)
        {
            var width = record.GetInt32("width");
            var height = record.GetInt32("height");
            var stride = record.GetInt32("stride");
            var dataRecord = record.GetSerializationRecord("scan0") as ClassRecord;

            if (dataRecord is null)
                throw new InvalidDataException("Invalid surface structure.");

            return new PdnSurface(width, height, stride, ReadMemoryBlock(dataRecord, context));
        }

        private static PdnLayerProperties ReadLayerProperties(ClassRecord record)
        {
            var name = record.GetString("name") ?? "";
            var isVisible = record.GetBoolean("visible");
            var isBackground = record.GetBoolean("isBackground");
            var opacity = record.GetByte("opacity");
            var blendModeRecord = record.GetSerializationRecord("blendMode") as ClassRecord;
            var userMetadataRecord = record.GetSerializationRecord("userMetadataItems") as ArrayRecord;

            if (blendModeRecord is null || userMetadataRecord is null)
                throw new InvalidDataException("Invalid layer properties structure.");

            return new PdnLayerProperties(name, isVisible, isBackground, opacity, ReadLayerBlendMode(blendModeRecord), ReadKeyValuePairs(userMetadataRecord));
        }

        private static PdnLayerBlendMode ReadLayerBlendMode(ClassRecord record)
            => (PdnLayerBlendMode)record.GetInt32("value__");

        private static PdnMemoryBlock ReadMemoryBlock(ClassRecord record, PdnReaderContext context)
        {
            var length = record.GetInt64("length64");
            var hasParent = record.GetBoolean("hasParent");
            var isDeferred = record.GetBoolean("deferred");

            if (!isDeferred)
                throw new NotSupportedException("Files with non-deferred memory blocks are not supported (yet).");

            var memoryBlock = new PdnMemoryBlock(length);
            context.MemoryBlockDeserializer.Register(memoryBlock);
            return memoryBlock;
        }

        private static KeyValuePair<string, string>[] ReadKeyValuePairs(ArrayRecord array)
        {
            var items = array.GetArray(typeof(KeyValuePair<string, string>[])) as object[];
            if (items is null)
                throw new InvalidDataException("Invalid metadata structure.");

            return items.OfType<ClassRecord>()
                .Select(ReadKeyValuePair)
                .ToArray();
        }

        private static KeyValuePair<string, string> ReadKeyValuePair(ClassRecord record)
            => new KeyValuePair<string, string>(record.GetString("key") ?? "", record.GetString("value") ?? "");

        private static Version ReadVersion(ClassRecord record)
        {
            var major = record.GetInt32("_Major");
            var minor = record.GetInt32("_Minor");
            var build = record.GetInt32("_Build");
            var revision = record.GetInt32("_Revision");

            return new Version(major, minor, build, revision);
        }


        private class PdnReaderContext
        {
            public PdnMemoryBlockDeserializer MemoryBlockDeserializer { get; }


            public PdnReaderContext(PdnMemoryBlockDeserializer memoryBlockDeserializer)
            {
                MemoryBlockDeserializer = memoryBlockDeserializer;
            }
        }
    }
}
