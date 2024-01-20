using System.Runtime.InteropServices;
using static Zip.Shared;

namespace Zip.Core;

// These are mostly ignored in this library
[StructLayout(LayoutKind.Sequential, Pack = 2)]
internal record struct LocalFileHeader
{
    public const int Tag = 0x04034b50;

    public int _tag;
    public short _versionRequiredToExtract;
    public GeneralPurposeFlags _generalPurposeFlag;
    public CompressionMethod _compressionMethod;
    public short _lastFileTime;
    public short _lastFileDate;
    public uint _crc32;
    public int _compressedSize;
    public int _uncompressedSize;
    public ushort _fileNameLength;
    public ushort _extraFieldLength;
}