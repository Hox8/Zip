using System;

namespace Zip.Core.Exceptions;

public class ZipException(string? message = null) : Exception(message);
public class InvalidZipException(string? message = null) : ZipException(message);
public class MalformedZipException(string? message = null) : ZipException(message);
public class UnsupportedCompressionException(string? message = null) : ZipException(message);
public class EncryptedEntriesException(string? message = null) : ZipException(message);
public class FailedCrcException(string? message = null) : ZipException(message);