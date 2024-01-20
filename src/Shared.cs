using System;

namespace Zip;

public static class Shared
{
    // These are all pretty messily dumped here

    public enum ZipVersion : byte
    {
        Default = 10,

        FEATURE_VolumeLabel = 11,

        FEATURE_Directory = 20,
        COMPRESS_Deflate = 20,
        ENCRYPT_PKWare = 20,


        COMPRESS_Deflate64 = 21,

        COMPRESS_Implode = 25,

        FEATURE_PatchData = 27,

        FEATURE_Zip64 = 45,

        COMPRESS_BZip2 = 46,

        ENCRYPT_DES = 50,
        ENCRYPT_3DES = 50,
        ENCRYPT_RC2 = 50,
        ENCRYPT_RC4 = 50,

        ENCRYPT_AES = 51,
        ENCRYPT_RC2Fixed = 52,

        ENCRYPT_NonOAEP = 61,

        FEATURE_CentralDirectoryEncryption = 62,

        COMRPESS_LZMA = 63,
        COMPRESS_PPMd = 63,
        COMPRESS_Blowfish = 63,
        COMPRESS_Twofish = 63
    }

    public enum AttributeCompatibility : byte
    {
        MSDOS = 0,
        UNIX = 3
    }

    [Flags]
    public enum GeneralPurposeFlags : ushort
    {
        None = 0,

        /// <summary>
        /// If set, indicates that the file is encrypted.
        /// </summary>
        Encrypted = 1 << 0,

        // The meaning of these change depending on compression type: Imploding, Deflate, and LZMA
        General0 = 1 << 1,
        General1 = 1 << 2,

        /// <summary>
        /// If this bit is set, the fields crc-32, compressed size and uncompressed size are set to zero in the local header.<br/>
        /// The correct values are put in the data descriptor immediately following the compressed data.
        /// </summary>
        /// <remarks>
        /// Note: PKZIP version 2.04g for DOS only recognizes this bit for method 8 compression, newer versions of PKZIP recognize this bit for any compression method.
        /// </remarks>
        ZeroedSizeStats = 1 << 3,

        /// <summary>
        /// Reserved for use with method 8, for enhanced deflating. 
        /// </summary>
        EnhancedDeflation = 1 << 4,

        /// <summary>
        /// If this bit is set, this indicates that the file is compressed patched data.<br/>
        /// </summary>
        /// <remarks>
        /// Note: Requires PKZIP version 2.70 or greater.
        /// </remarks>
        CompressedPatchedData = 1 << 5,

        /// <summary>
        /// Strong encryption. If this bit is set, you MUST set the version needed to extract value to at least 50 and you MUST also set bit 0.
        /// If AES encryption is used, the version needed to extract value MUST be at least 51.
        /// </summary>
        StrongEncryption = 1 << 6,

        // Bits 7 through 10 are unused

        /// <summary>
        /// Language encoding flag (EFS).<br/>
        /// If this bit is set, the filename and comment fields for this file MUST be encoded using UTF-8.
        /// </summary>
        UTF8Encoded = 1 << 11,

        /// <summary>
        /// Reserved by PKWARE for enhanced compression.
        /// </summary>
        EnhancedCompression = 1 << 12,

        /// <summary>
        /// Set when encrypting the Central Directory to indicate selected data values in the Local Header are masked to hide their actual values.<br/>
        /// </summary>
        MaskedDataValues = 1 << 13,

        PkReserved0 = 1 << 14,
        PkReserved1 = 1 << 15,
    }

    public enum CompressionMethod : short
    {
        /// <summary>File is stored (no compression).</summary>
        None = 0,
        /// <summary>File is Shrunk.</summary>
        Shrunk = 1,
        /// <summary>File is Reduced with compression factor 1.</summary>
        Reduced1 = 2,
        /// <summary>File is Reduced with compression factor 2.</summary>
        Reduced2 = 3,
        /// <summary>File is Reduced with compression factor 3.</summary>
        Reduced3 = 4,
        /// <summary>File is Reduced with compression factor 4.</summary>
        Reduced4 = 5,
        /// <summary>File is Imploded.</summary>
        Imploded = 6,
        /// <summary>Reserved for Tokenizing compression algorithm.</summary>
        Reserved0 = 7,
        /// <summary>File is Deflated.</summary>
        Deflate = 8,
        /// <summary>Enhanced Deflating using Deflate64(tm).</summary>
        Deflate64 = 9,
        ///<summary>PKWARE Data Compression Library Imploding (old IBM TERSE).</summary>
        TerseOld = 10,
        ///<summary>Reserved by PKWARE.</summary>
        Reserved1 = 11,
        ///<summary>File is compressed using BZIP2 algorithm.</summary>
        BZip2 = 12,
        ///<summary>Reserved by PKWARE.</summary>
        Reserved2 = 13,
        ///<summary>LZMA.</summary>
        LZMA = 14,
        ///<summary>Reserved by PKWARE.</summary>
        Reserved3 = 15,
        ///<summary>IBM z/OS CMPSC Compression.</summary>
        CMPSC = 16,
        ///<summary>Reserved by PKWARE.</summary>
        Reserved4 = 17,
        ///<summary>File is compressed using IBM TERSE (new).</summary>
        TerseNew = 18,
        ///<summary>IBM LZ77 z Architecture.</summary>
        LZ77 = 19,
        ///<summary>Deprecated (use method 93 for ZStd).</summary>
        ZStdDeprecated = 20,
        ///<summary>ZStandard (ZStd) Compression.</summary>
        ZStd = 93,
        ///<summary>MP3 Compression.</summary>
        MP3 = 94,
        ///<summary>XZ Compression.</summary>
        XZ = 95,
        ///<summary>JPEG variant.</summary>
        JPEG = 96,
        ///<summary>WavPack compressed data.</summary>
        WavPack = 97,
        ///<summary>PPMd version I, Rev 1.</summary>
        PPMd = 98,
        ///<summary>AE-x encryption marker (see APPENDIX E).</summary>
        AEx = 99,
    }
}
