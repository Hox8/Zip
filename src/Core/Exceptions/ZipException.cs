using System;
using System.Diagnostics.CodeAnalysis;

namespace Zip.Core.Exceptions;

public enum ZipExceptionType
{
    InvalidZip,
    MalformedZip,
    UnsupportedCompression,
    EncryptedEntries,
    FailedCrc,

    EntryLacksOriginalData
}

public class ZipException(ZipExceptionType type) : Exception
{
    public readonly ZipExceptionType Type = type;

    public static void Assert(bool expression, ZipExceptionType type)
    {
        if (!expression) Throw(type);
    }

    [DoesNotReturn]
    private static void Throw(ZipExceptionType type) => throw new ZipException(type);
}
