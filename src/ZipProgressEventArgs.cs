using System;

namespace Zip.Core.Events;

// Saving uses a 'weighted' progress biased toward bytes requiring compression (1:25)
// This is because compression involves much more work than simply writing bytes, so
// multiplying their contribution to the total by x25 seems like a fair approximation.

// Progress reporting associated with saving a ZipArchive.
public sealed class ZipSaveProgressEventArgs : EventArgs
{
    private const int ReadWriteWeight = 1;
    private const int CompressionWeight = 25;

    internal readonly long TotalBytesToWrite;
    internal readonly long TotalBytesToCompress;
    internal readonly long TotalWeightedWork;

    internal long CurrentBytesWritten;
    internal long CurrentBytesCompressed;

    public ZipSaveProgressEventArgs(long totalBytesToWrite, long totalBytesToCompress)
    {
        TotalBytesToWrite = totalBytesToWrite;
        TotalBytesToCompress = totalBytesToCompress;

        TotalWeightedWork = (TotalBytesToWrite * ReadWriteWeight) + (TotalBytesToCompress * CompressionWeight);
    }

    public long GetTotalBytesToWrite() => TotalBytesToWrite;

    public float GetProgress()
    {
        if (TotalWeightedWork == 0)
        {
            return 1.0f;
        }

        return ((CurrentBytesWritten * ReadWriteWeight) + (CurrentBytesCompressed * CompressionWeight)) / (float)TotalWeightedWork;
    }
}
