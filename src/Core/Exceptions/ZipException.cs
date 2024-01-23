using System;
using System.Diagnostics.CodeAnalysis;

namespace Zip.Core.Exceptions;

public enum ZipExceptionType
{
    InvalidZip,
    MalformedZip,
    UnsupportedCompression,
    EncryptedEntries,
    FailedCrc
}

public class ZipException(ZipExceptionType type) : Exception
{
    public ZipExceptionType Type { get; private set; } = type;

    [DoesNotReturn]
    private static void Throw(ZipExceptionType type) => throw new ZipException(type);

    public static void Assert(bool expression, ZipExceptionType type)
    {
        if (!expression) Throw(type);
    }
}
