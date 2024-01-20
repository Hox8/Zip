using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Zip.Core.Exceptions;
using static Zip.Shared;

namespace Zip.Core;

public class ZipEntry
{
    internal CentralDirectory _data;
    internal string? PathToUpdatedContents;

    #region Accessors

    public ZipVersion VersionMadeBy { get => _data._versionMadeBy; set => _data._versionMadeBy = value; }
    public AttributeCompatibility AttributeCompatibility { get => _data._attributeCompatibility; set => _data._attributeCompatibility = value; }
    public short VersionRequiredToExtract { get => _data._versionRequiredToExtract; set => _data._versionRequiredToExtract = value; }
    public GeneralPurposeFlags GeneralPurposeFlags { get => _data._generalPurposeFlag; set => _data._generalPurposeFlag = value; }
    public CompressionMethod CompressionMethod { get => _data._compressionMethod; set => _data._compressionMethod = value; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.SmallestSize;
    public int CompressedSize { get => _data._compressedSize; set => _data._compressedSize = value; }
    public int UncompressedSize { get => _data._uncompressedSize; set => _data._uncompressedSize = value; }

    internal const UnixFileMode DefaultFilePermissions =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    internal const UnixFileMode DefaultDirectoryPermissions =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    internal const FileAttributes DefaultFileAttributes = FileAttributes.Normal;

    public FileAttributes FileAttributes
    {
        get => (FileAttributes)_data._externalFileAttributesLow;
        set => _data._externalFileAttributesLow = (ushort)value;
    }
    public UnixFileMode UnixFileMode
    {
        // FileType is stored in the first bit. Permissions are stored in the last three.
        get => (UnixFileMode)(_data._externalFileAttributesHigh & 0x0FFF);
        set => _data._externalFileAttributesHigh = (ushort)((ushort)value & 0x0FFF);
    }

    public bool IsDirectory => (FileAttributes & FileAttributes.Directory) != 0;

    internal bool WantsUpdate => PathToUpdatedContents is not null;

    private readonly ZipArchive _progenitor;

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            _data._fileNameLength = (ushort)Encoding.UTF8.GetByteCount(value);
        }
    }

    private string _comment;
    public string Comment
    {
        get => _comment;
        set
        {
            _comment = value;
            _data._fileCommentLength = (ushort)Encoding.UTF8.GetByteCount(value);
        }
    }

    public DateTime LastFileTime
    {
        get
        {
            int second = (_data._lastFileTime & 0x1F) * 2;
            int minute = (_data._lastFileTime >> 5) & 0x3F;
            int hour = (_data._lastFileTime >> 11) & 0x1F;

            int day = _data._lastFileDate & 0x1F;
            int month = (_data._lastFileDate >> 5) & 0x0F;
            int year = ((_data._lastFileDate >> 9) & 0x7F) + 1980;

            return new DateTime(year, month, day, hour, minute, second);
        }
        set
        {
            int year = value.Year;
            int month = value.Month;
            int day = value.Day;

            int hour = value.Hour;
            int minute = value.Minute;
            int second = value.Second / 2;

            int msDosDate = ((year - 1980) << 9) | (month << 5) | day;
            int msDosTime = (hour << 11) | (minute << 5) | second;

            _data._lastFileDate = (short)msDosDate;
            _data._lastFileTime = (short)msDosTime;
        }
    }

    public override string ToString() => _name;

    #endregion

    #region Constructors

    private ZipEntry(ZipArchive archive) => _progenitor = archive;

    internal static ZipEntry ReadFromArchive(ZipArchive archive)
    {
        var entry = new ZipEntry(archive);
        archive._buffer.Read(ref entry._data);

        if (entry._data._tag != CentralDirectory.Tag)
            throw new MalformedZipException();

        entry._name = archive._buffer.ReadString(entry._data._fileNameLength);
        entry._comment = archive._buffer.ReadString(entry._data._fileCommentLength);

        // Skip any extra fields. Mostly higher precision time data
        archive._buffer.Position += entry._data._extraFieldLength;

        // Convert OS attributes if needed

        if (entry._data._attributeCompatibility is AttributeCompatibility.UNIX)
        {
            // Check for Unix directory bit. We do this on both Windows and Unix machines
            entry.FileAttributes = ((entry._data._externalFileAttributesHigh >> 12) & 0xF) == 4
                ? FileAttributes.Directory
                : FileAttributes.Normal;
        }
        else if (entry._data._attributeCompatibility is not AttributeCompatibility.MSDOS)
        {
            // Unknown. Reset attributes to default values
            entry.FileAttributes = DefaultFileAttributes;
        }

#if UNIX
        // Since we're running on Unix, we need to set default permissions
        if (entry._data._attributeCompatibility is not AttributeCompatibility.UNIX)
        {
            // Set permissions based on whether we've got a file or a directory
            entry.UnixFileMode = entry.IsDirectory ? DefaultDirectoryPermissions : DefaultFilePermissions;
        }
#endif

        // Filenames starting with '.' are implicitly hidden on Unix machines
        if (entry.IsDirectory && entry._name[0] == '.' || !entry.IsDirectory && Path.GetFileName(entry._name)[0] == '.')
        {
            entry.FileAttributes |= FileAttributes.Hidden;
        }

        return entry;
    }

    public static ZipEntry CreateNew(ZipArchive archive, string name) => new(archive)
    {
        _data = new CentralDirectory
        {
            _tag = CentralDirectory.Tag,
            _versionMadeBy = ZipVersion.Default,
#if UNIX
            _attributeCompatibility = AttributeCompatibility.UNIX,
#else
            _attributeCompatibility = AttributeCompatibility.MSDOS
#endif
        },

        FileAttributes = DefaultFileAttributes,

#if UNIX
        UnixFileMode = DefaultFilePermissions,
#endif

        Name = name,
        Comment = ""
    };

    #endregion

    public void Update(string path)
    {
        PathToUpdatedContents = path;

        if (!_progenitor._entriesToUpdate.Contains(this))
        {
            _progenitor._entriesToUpdate.Add(this);
        }
    }

    public void Extract(string basePath)
    {
        basePath = Path.Combine(basePath, Path.GetDirectoryName(_name));
        Directory.CreateDirectory(basePath);

        if (IsDirectory)
        {
            // Set directory attributes
            _ = new DirectoryInfo(basePath)
            {
                Attributes = FileAttributes.None,
#if UNIX
                UnixFileMode = UnixFileMode,
#endif
                LastAccessTime = LastFileTime,
                LastWriteTime = LastFileTime,
                CreationTime = LastFileTime
            };

            // Do not process directories further
            return;
        }

        string filePath = Path.Combine(basePath, Path.GetFileName(_name));
        using var fileStream = File.Create(filePath);

        // Skip extracting data on empty files
        if (UncompressedSize > 0)
        {
            // Create a new stream based off the archive's to support parallelization
            Stream reader = File.OpenRead((_progenitor._buffer).Name);

            reader.Position = _data._relativeOffsetOfLocalHeader;
            ReadAndSkipOverLocalFileHeader(reader);

            // If data is deflated, set up inflate stream (done after positioning since DeflateStream doesn't support seeking)
            if (CompressionMethod is CompressionMethod.Deflate)
            {
                reader = new DeflateStream(reader, CompressionMode.Decompress);
            }

            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);

            int remaining = UncompressedSize;
            while (remaining != 0)
            {
                int bytesRead = reader.Read(buffer, 0, Math.Min(remaining, buffer.Length));
                remaining -= bytesRead;
                fileStream.Write(buffer, 0, bytesRead);
            }

            ArrayPool<byte>.Shared.Return(buffer);
            reader.Dispose();
        }

        // Set file attributes
        File.SetAttributes(fileStream.SafeFileHandle, FileAttributes);
#if UNIX
            File.SetUnixFileMode(fileStream.SafeFileHandle, UnixFileMode);
#endif
        File.SetLastWriteTime(fileStream.SafeFileHandle, LastFileTime);
        File.SetCreationTime(fileStream.SafeFileHandle, LastFileTime);
        File.SetLastAccessTime(fileStream.SafeFileHandle, LastFileTime);
    }

    // Sets/updates fields in preparation for saving
    // @TODO this is messy copout. Do something proper
    internal void PrepareForSave()
    {
        _data._versionMadeBy = ZipArchive.Version;
        _data._versionRequiredToExtract = (short)(_data._compressionMethod is CompressionMethod.Deflate ? 20 : 10);

#if UNIX
        _data._attributeCompatibility = AttributeCompatibility.UNIX;
#else
        _data._attributeCompatibility = AttributeCompatibility.MSDOS;
#endif

        _data._generalPurposeFlag = (Ascii.IsValid(_name) && Ascii.IsValid(_comment))
            ? GeneralPurposeFlags.None
            : GeneralPurposeFlags.UTF8Encoded;

        // Extra fields are out of scope for now. We're tossing any we come across
        _data._extraFieldLength = 0;
    }

    internal unsafe void WriteLocalHeader(Stream stream)
    {
        LocalFileHeader header = new()
        {
            _tag = LocalFileHeader.Tag,
            _versionRequiredToExtract = _data._versionRequiredToExtract,
            _generalPurposeFlag = _data._generalPurposeFlag,
            _compressionMethod = _data._compressionMethod,
            _lastFileTime = _data._lastFileTime,
            _lastFileDate = _data._lastFileDate,
            _crc32 = _data._crc32,
            _compressedSize = _data._compressedSize,
            _uncompressedSize = _data._uncompressedSize,
            _fileNameLength = _data._fileNameLength,
            _extraFieldLength = _data._extraFieldLength,
        };

        stream.Write(header);
        stream.WriteString(_name);

        Debug.Assert(_data._extraFieldLength == 0, "Not accounting for extra fields");
    }

    internal static unsafe void ReadAndSkipOverLocalFileHeader(Stream stream)
    {
        LocalFileHeader header = default;
        stream.Read(ref header);

        Debug.Assert(header._tag == LocalFileHeader.Tag);

        stream.Position += header._fileNameLength + header._extraFieldLength;
    }
}
