using System;

namespace Zip.Core.Events;

public abstract class ZipProgressEventArgs(long totalBytes, long processedBytes) : EventArgs
{
    public readonly long TotalBytes = totalBytes;
    public long ProcessedBytes = processedBytes;

    public float Progress => ProcessedBytes / (float)TotalBytes;
}

// Saving uses a 'weighted' progress biased toward bytes requiring compression (1:25)
// This is because compression involves much more work than simply writing bytes, so
// multiplying their contribution to the total by x25 seems like a fair approximation.
public class SaveProgressEventArgs(long bytesToWrite, long bytesToCompress, long processedBytes) : ZipProgressEventArgs(bytesToWrite + bytesToCompress, processedBytes)
{
    internal readonly long _totalBytesWeighted = (bytesToWrite * WeightingWritten) + (bytesToCompress * WeightingCompressed);

    public readonly long TotalBytesToWrite = bytesToWrite;
    public readonly long TotalBytesToCompress = bytesToCompress;

    internal long _bytesWritten;
    internal long _bytesCompressed;

    public long BytesWritten => _bytesWritten;
    public long BytesCompressed => _bytesCompressed;

    public const int WeightingCompressed = 25;
    public const int WeightingWritten = 1;

    public float ProgressWeighted => ((_bytesWritten * WeightingWritten) + (_bytesCompressed * WeightingCompressed)) / (float)_totalBytesWeighted;
}

public class ExtractProgressEventArgs(long totalBytes, long processedBytes) : ZipProgressEventArgs(totalBytes, processedBytes);