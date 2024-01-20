using System.Runtime.InteropServices;
using static Zip.Shared;

namespace Zip.Core;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal record struct CentralDirectory
{
    internal const int Tag = 0x02014b50;

    internal int _tag;
    internal ZipVersion _versionMadeBy;
    internal AttributeCompatibility _attributeCompatibility;
    internal short _versionRequiredToExtract;
    internal GeneralPurposeFlags _generalPurposeFlag;
    internal CompressionMethod _compressionMethod;
    internal short _lastFileTime;
    internal short _lastFileDate;
    internal uint _crc32;
    internal int _compressedSize;
    internal int _uncompressedSize;
    internal ushort _fileNameLength;
    internal ushort _extraFieldLength;
    internal ushort _fileCommentLength;
    internal short _diskNumberStart;
    internal ushort _internalFileAttributes;
    internal ushort _externalFileAttributesLow;
    internal ushort _externalFileAttributesHigh;
    internal int _relativeOffsetOfLocalHeader;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal record struct EndOfCentralDirectory
{
    public const int Tag = 0x06054b50;

    public int _tag;
    /// <summary>The number of this disk.</summary>
    public short _diskNumber;
    /// <summary>Number of the disk on which the Central Directory starts.</summary>
    public short _diskStart;
    /// <summary>Number of Central Directory entries on this disk.</summary>
    public short _numEntriesOnDisk;
    /// <summary>Total number of Central Directory entries.</summary>
    public short _numEntriesTotal;
    /// <summary>Size of the Central Directory, in bytes.</summary>
    public int _centralDirectorySize;
    /// <summary>Offset to the start of the Central Directory, relative to the current disk.</summary>
    public int _centralDirectoryOffset;
    /// <summary>Length of the following string comment field.</summary>
    public short _commentLength;
}
