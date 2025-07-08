using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using Zip.Core;
using Zip.Core.Exceptions;

#if WITH_EXTRAFIELDS
using Zip.Core.ExtraFields;
#endif

using static Zip.Core.ZipConstants;

namespace Zip;

/// <summary>
/// Represents an entry within a <see cref="ZipArchive"/>.
/// </summary>
public class ZipEntry
{
    private CentralDirectory _data;
    private string _name;
    private string _comment;

#if WITH_EXTRAFIELDS
    private ExtraFieldBase _extraField;
#endif

    internal readonly ZipEntryDataDescriptor FileData;

    #region Accessors

    #region Naming

    public string Name
    {
        get => _name;
        set => _name = value ?? "";
    }

    public string Comment
    {
        get => _comment;
        set => _comment = value ?? "";
    }

    public bool HasValidComment() => _comment.Length > 0;

    internal int NameSize => _data._fileNameLength;

    #endregion

    #region Compression

    public CompressionMethod CompressionMethod { get => _data._compressionMethod; set => _data._compressionMethod = value; }
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.SmallestSize;

    #endregion

    #region Attributes

    internal const UnixFileMode DefaultUnixFilePermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    internal const UnixFileMode DefaultUnixDirectoryPermissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

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

    #endregion

    public bool IsEncrypted => (FileAttributes & FileAttributes.Encrypted) != 0;
    public bool IsDirectory => (FileAttributes & FileAttributes.Directory) != 0;

    public int CompressedSize { get => _data._compressedSize; set => _data._compressedSize = value; }
    public int UncompressedSize { get => _data._uncompressedSize; set => _data._uncompressedSize = value; }

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

    internal uint CRC32 { get => _data._crc32; set => _data._crc32 = value; }
    internal ushort ExtraFieldLength { get => _data._extraFieldLength; set => _data._extraFieldLength = value; }
    internal int Offset { get => _data._relativeOffsetOfLocalHeader; }

    #endregion

    #region Constructors

    private ZipEntry(string entryName, string entryComment = "", bool bIsDirectory = false)
    {
        // Sets the tag
        _data = new CentralDirectory();

        FileData = new ZipEntryDataDescriptor();

        // Deflate by default
        CompressionMethod = CompressionMethod.Deflate;

        // Set default file attributes
        FileAttributes = bIsDirectory ? FileAttributes.Directory : FileAttributes.Normal;

#if UNIX
        // Set default Unix permissions
        UnixFileMode = bIsDirectory ? DefaultUnixDirectoryPermissions : DefaultUnixFilePermissions;
#endif

        _name = entryName;
        _comment = entryComment;

#if WITH_EXTRAFIELDS
        _extraField = new EmptyField();
#endif
    }

    private unsafe ZipEntry(FileStream stream)
    {
        stream.Read(ref _data);

        // Cache original offset now, since these fields could change between now and save
        int offset = _data._relativeOffsetOfLocalHeader + sizeof(LocalFileHeader) + _data._fileNameLength + _data._fileCommentLength + _data._extraFieldLength;
        FileData = new ZipEntryDataDescriptor(stream.Name, offset);

        // Throw if this ZipEntry is invalid or uses a feature we do not support
        Validate();

        _name = stream.ReadString(_data._fileNameLength);
        _comment = stream.ReadString(_data._fileCommentLength);

#if WITH_EXTRAFIELDS
        if (ExtraFieldLength > 0)
        {
            _extraField = ExtraFieldBase.Read(stream, this);
        }
#else
        // Skip over any extra fields
        stream.Position += _data._extraFieldLength;
        _data._extraFieldLength = 0;
#endif

        if (_data._attributeCompatibility == AttributeCompatibility.UNIX)
        {
            // Infer attributes from the Unix directory bit
            FileAttributes = ((_data._externalFileAttributesHigh >> 12) & 0xF) == 4
                ? FileAttributes.Directory
                : FileAttributes.Normal;
        }
        else
        {
            if (_data._attributeCompatibility != AttributeCompatibility.MSDOS)
            {
                // Attribute compatibility is unknown, so we reset them to safe defaults
                FileAttributes = FileAttributes.Normal;
            }

#if UNIX
            // Set default Unix permissions
            UnixFileMode = IsDirectory ? DefaultUnixDirectoryPermissions : DefaultUnixFilePermissions;
#endif
        }
    }

    public static ZipEntry Create(string entryName, string entryComment = "", bool bIsDirectory = false) => new(entryName, entryComment, bIsDirectory);
    internal static ZipEntry FromStream(FileStream stream) => new(stream);

#endregion

    #region Public API

    /// <summary>
    /// Mark that this ZipEntry should acquire new file contents on its ZipArchive's next save.
    /// </summary>
    /// <remarks>The specified path should remain accessible until after the ZipArchive has saved.</remarks>
    /// <param name="newContentsPath">The path on-disk containing the desired data.</param>
    public void UpdateContents(string newContentsPath)
    {
        FileData.SetPath(newContentsPath);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="destinationPath">Where this ZipEntry should be extracted to, on-disk.</param>
    public void Extract(string destinationPath)
    {
        // Throw if we don't have any original data.
        ZipException.Assert(FileData.HasOriginalData(), ZipExceptionType.EntryLacksOriginalData);

        var fullDiskPath = Path.Combine(destinationPath, Name);
        FileSystemInfo fileInfo = IsDirectory ? new DirectoryInfo(fullDiskPath) : new FileInfo(fullDiskPath);

        if (IsDirectory)
        {
            ((DirectoryInfo)fileInfo).Create();
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullDiskPath));
            using FileStream destination = ((FileInfo)fileInfo).Create();

            // Is there data to extract?
            if (UncompressedSize > 0)
            {
                // Open a filestream pointing to our owning ZipArchive's stream
                using FileStream reader = File.OpenRead(FileData.OriginalPath);

                // Position ourselves to the start of our data payload
                reader.Position = FileData.OriginalOffset;

                if (CompressionMethod == CompressionMethod.Deflate)
                {
                    using var inflator = new DeflateStream(reader, CompressionMode.Decompress);

                    inflator.ConstrainedCopy(destination, UncompressedSize);
                }
                else
                {
                    Debug.Assert(CompressionMethod == CompressionMethod.None);

                    reader.ConstrainedCopy(destination, UncompressedSize);
                }
            }
        }

        // Set attributes
        {
            fileInfo.Attributes = FileAttributes;
#if UNIX
            fileInfo.UnixFileMode = UnixFileMode;
#endif
            fileInfo.LastAccessTime = LastFileTime;
            fileInfo.LastWriteTime = LastFileTime;
            fileInfo.CreationTime = LastFileTime;
        }
    }

    #endregion

    // Called by the constructor. Throws if we encounter a ZipEntry using features we do not support
    private void Validate()
    {
        // Ensure we've read a valid CentralDirectory
        ZipException.Assert(_data._tag == CentralDirectory.Tag, ZipExceptionType.MalformedZip);

        // Throw if entry is encrypted
        ZipException.Assert(!IsEncrypted, ZipExceptionType.EncryptedEntries);

        // Throw if entry uses an unsupported compression scheme
        ZipException.Assert(_data._compressionMethod is (CompressionMethod.Deflate or CompressionMethod.None), ZipExceptionType.UnsupportedCompression);

        // We aren't checking CRCs here because it would be too slow--files must be decompressed in order to calculate CRCs.
        // Delegate this and other expensive checks to ZipEntry.Extract()?
    }

    // Gets the Zip version required to extract this ZipEntry, depending on its used features
    private ZipVersion GetVersionRequiredToExtract()
    {
        if (_data._compressionMethod == CompressionMethod.Deflate)
        {
            return ZipVersion.COMPRESS_Deflate;
        }
        else
        {
            Debug.Assert(_data._compressionMethod == CompressionMethod.None, "Unsupported ZipEntry compression scheme");
        }

        return ZipVersion.Default;
    }

    internal void CacheValuesForSave(Stream stream)
    {
        _data._versionMadeBy = ZipArchive.Version;
        _data._versionRequiredToExtract = (short)GetVersionRequiredToExtract();

#if UNIX
        _data._attributeCompatibility = AttributeCompatibility.UNIX;
#else
        _data._attributeCompatibility = AttributeCompatibility.MSDOS;
#endif

#if UNIX
        // Set directory bit for Unix zips
        if ((FileAttributes & FileAttributes.Directory) != 0)
        {
            _data._externalFileAttributesHigh |= (4 << 12);
        }
#endif

        _data._fileNameLength = (ushort)Encoding.UTF8.GetByteCount(_name);
        _data._fileCommentLength = (ushort)Encoding.UTF8.GetByteCount(_comment);

        _data._generalPurposeFlag = (Ascii.IsValid(_name) && Ascii.IsValid(_comment))
           ? GeneralPurposeFlags.None
           : GeneralPurposeFlags.UTF8Encoded;

        _data._relativeOffsetOfLocalHeader = (int)stream.Position;
    }

    internal void WriteLocalHeader(Stream stream)
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

        stream.Write(ref header);
        stream.WriteString(_name);
    }

    internal void WriteCentralDirectory(Stream stream)
    {
        stream.Write(ref _data);

        stream.WriteString(_name);

        if (HasValidComment())
        {
            stream.WriteString(_comment);
        }
    }
}
