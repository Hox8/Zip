using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Zip.Core;
using Zip.Core.Events;
using Zip.Core.Exceptions;
using static Zip.Core.ZipConstants;

namespace Zip;

public class ZipArchive : IDisposable
{
    public const ZipVersion Version = ZipVersion.COMPRESS_Deflate;

    private EndOfCentralDirectory _data;
    public readonly List<ZipEntry> Entries;

    // This is used only when opening an existing zip archive.
    // We keep this handle alive for the lifetime of this ZipArchive,
    // which guarantees we can also refer to original ZipEntry data, and
    // also allows us to reuse this handle when saving unmodified ZipEntries.
    private readonly FileStream? _inputFile;

    private bool _bIsDisposed;

    #region Config

    // The minimum acceptable compression ratio to use when compressing new files.
    // If this is not met, files will be stored uncompressed regardless of compression settings.
    public float MinimumAcceptableCompressionRatio = 0.9f;

    // I'd like to support this, but I'll need to change how a few things work.
    // public bool ForceFullRecompression;

    #endregion

    #region Events

    // ProgressChanged event for zip saving
    public delegate void ZipSaveProgressEventHandler(ZipArchive sender, ZipSaveProgressEventArgs e);

    /// <summary>
    /// Occurs during save operations to report overall save progress.
    /// </summary>
    /// <remarks>
    /// This event is fired each time a <see cref="ZipEntry"/> is compressed and/or written to disk.<br/>
    /// Overall progress is weighted; see <see cref="ZipSaveProgressEventArgs"/> for details.
    /// </remarks>
    public event ZipSaveProgressEventHandler? ZipSaveProgressChanged;

    #endregion

    #region Accessors

    // The last size of this zip archive file on-disk.
    public long Size { get; private set; }

    public string Comment { get; set; } = "";

    public bool HasValidComment() => Comment.Length > 0;

    #endregion

    #region Constructors

    private ZipArchive()
    {
        Entries = [];
        _data = new EndOfCentralDirectory();
    }

    private unsafe ZipArchive(FileStream stream)
    {
        // We are taking ownership of the stream
        _inputFile = stream;

        Size = stream.Length;

        // Zip archive must be big enough to store at least the EOCD
        ZipException.Assert(stream.Length >= sizeof(EndOfCentralDirectory), ZipExceptionType.InvalidZip);

        // Parse the End of Central Directory
        stream.Position = stream.Length - sizeof(EndOfCentralDirectory);
        stream.Read(ref _data);

        // If tag doesn't match, the zip archive could be using a zip comment
        if (_data._tag != EndOfCentralDirectory.Tag)
        {
            // Brute force check the last 64KB (ushort; maximum comment size)
            stream.Position = Math.Max(0, stream.Length - ushort.MaxValue);

            // Crawl until we hit EOF - 4, or we find signature
            while (stream.Position < stream.Length - sizeof(int) && !(
                   stream.ReadByte() == EndOfCentralDirectory.Tag0 &&
                   stream.ReadByte() == EndOfCentralDirectory.Tag1 &&
                   stream.ReadByte() == EndOfCentralDirectory.Tag2 &&
                   stream.ReadByte() == EndOfCentralDirectory.Tag3) );

            // Try read EOCD again if we didn't hit EOF
            if (stream.Position + sizeof(EndOfCentralDirectory) <= stream.Length)
            {
                stream.Position -= sizeof(int);
                stream.Read(ref _data);

                Comment = stream.ReadString(_data._commentLength);
            }

            // If the EOCD tag still doesn't match, this isn't a valid zip archive
            ZipException.Assert(_data._tag == EndOfCentralDirectory.Tag, ZipExceptionType.InvalidZip);
        }

        // Make sure this zip archive is actually big enough to store these records
        ZipException.Assert(stream.Length >= _data._centralDirectoryOffset + _data._centralDirectorySize, ZipExceptionType.InvalidZip);

        // Parse Central Directories
        Entries = new List<ZipEntry>(_data._numEntriesTotal);
        stream.Position = _data._centralDirectoryOffset;

        for (int i = 0; i < Entries.Capacity; i++)
        {
            Entries.Add(ZipEntry.FromStream(stream));
        }
    }

    public static ZipArchive Create() => new();
    public static ZipArchive FromFile(string filePath) => new(File.OpenRead(filePath));
    public static ZipArchive FromFile(FileStream fileStream) => new(fileStream);

    #endregion

    #region IO

    /// <summary>
    /// Saves the <see cref="ZipArchive"/> to the specified file path, compressing any entries marked for update.
    /// </summary>
    /// <param name="zipPath">The file path where the <see cref="ZipArchive"/> will be saved. If the file exists, it will be overwritten.</param>
    public unsafe void SaveToFile(string zipPath)
    {
        // Tally up sizes to set up progress events

        long totalBytesToWrite = 0;
        long totalBytesToCompress = 0;

        // Construct a fresh list of entries wanting updates here.
        List<ZipEntry> entriesWantingUpdates = [];

        foreach (var entry in Entries.AsSpan())
        {
            if (EntryWantsUpdate(entry))
            {
                // We don't know what the compressed size is at this point, so just use the uncompressed size
                long uncompressedSize = entry.FileData.WantsOriginalData()
                    ? entry.UncompressedSize
                    : new FileInfo(entry.FileData.GetPath()).Length;

                totalBytesToCompress += uncompressedSize;
                totalBytesToWrite += uncompressedSize;

                entriesWantingUpdates.Add(entry);
            }
            else
            {
                // For simplicity, we'll tally the uncompressed size regardless of any existing compression
                totalBytesToWrite += entry.UncompressedSize;
            }
        }

        // A central directory record needs to be written for every entry present, which represents a small but quantifiable unit of work.
        // We approximate this workload ahead of time by making it attribute to 1% of the overall progress (affected by weighting, so this may end up being smaller than 1%)
        int onePercentOfTotal = (int)(totalBytesToWrite / 100.0f);

        ZipSaveProgressEventArgs saveEvent = new(totalBytesToWrite + onePercentOfTotal, totalBytesToCompress);

        // Use a temporary path to store any intermediate files
        string tempFolderPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempFolderPath);

        // Compress and calculate CRCs for all entries queued for update
        UpdateEntriesInternal(entriesWantingUpdates, saveEvent, tempFolderPath);

        // Now, write the zip archive to disk
        string tempZipPath = Path.Combine(tempFolderPath, Path.GetRandomFileName());
        using (var outStream = File.Create(tempZipPath, BufferSize, FileOptions.SequentialScan))
        {
            // Write out entries
            foreach (var entry in Entries.AsSpan())
            {
                // Update entry values, and write out its local header
                entry.CacheValuesForSave(outStream);
                entry.WriteLocalHeader(outStream);

                // Write any entry data
                if (entry.FileData.HasData())
                {
                    // Should we re-use existing data?
                    if (entry.FileData.WantsOriginalData())
                    {
                        // Yes. As an optimization, we can reuse our persistent handle.
                        // Copy the relevant data at the entry's offset
                        _inputFile!.Position = entry.FileData.OriginalOffset;
                        _inputFile.ConstrainedCopy(outStream, entry.CompressedSize);
                    }
                    else
                    {
                        // Using RandomAccess over FileStream to reduce allocations, as this can be called many, many times
                        using var handle = File.OpenHandle(entry.FileData.GetTempPath(), FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.SequentialScan);

                        handle.ConstrainedCopy(outStream, entry.CompressedSize, 0);
                    }
                }

                // Progress event
                saveEvent.CurrentBytesWritten += entry.UncompressedSize;
                ZipSaveProgressChanged?.Invoke(this, saveEvent);
            }

            // Write out central directories

            _data._centralDirectoryOffset = (int)outStream.Position;

            foreach (var entry in Entries.AsSpan())
            {
                entry.WriteCentralDirectory(outStream);
            }

            // Update and write end of central directory
            _data._centralDirectorySize = (int)outStream.Position - _data._centralDirectoryOffset;
            _data._numEntriesTotal = (short)Entries.Count;
            _data._numEntriesOnDisk = _data._numEntriesTotal;
            _data._commentLength = (ushort)Encoding.UTF8.GetByteCount(Comment);

            outStream.Write(ref _data);

            if (HasValidComment())
            {
                outStream.WriteString(Comment);
            }

            // Update our size
            Size = outStream.Length;
        }

        // Verify our progress has progressed as expected
        Debug.Assert(saveEvent.CurrentBytesCompressed == saveEvent.TotalBytesToCompress);
        Debug.Assert(saveEvent.CurrentBytesWritten + onePercentOfTotal == saveEvent.TotalBytesToWrite);

        // We're finished. Set our current stats to their totals and broadcast one last time
        saveEvent.CurrentBytesWritten = saveEvent.TotalBytesToWrite;
        ZipSaveProgressChanged?.Invoke(this, saveEvent);

        // Move the temp zip to its destination
        File.Move(tempZipPath, zipPath, true);

        // Clear out our temp folder
        Directory.Delete(tempFolderPath, true);
    }

    #endregion

    #region Public API

    public bool TryGetEntry(string zipEntryPath, [NotNullWhen(true)] out ZipEntry? outEntry)
    {
        // Zip paths must use Unix path separator
        zipEntryPath = zipEntryPath.Replace('\\', '/');

        foreach (var entry in Entries.AsSpan())
        {
            if (string.Equals(entry.Name, zipEntryPath, StringComparison.OrdinalIgnoreCase))
            {
                outEntry = entry;
                return true;
            }
        }

        outEntry = default;
        return false;
    }

    public void UpdateEntry(string filePath, string zipPath)
    {
        if (!TryGetEntry(filePath, out ZipEntry? outEntry))
        {
            outEntry = ZipEntry.Create(zipPath);
            Entries.Add(outEntry);
        }

        outEntry.UpdateContents(filePath);
    }

    public void UpdateEntries(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories))
        {
            var zipPath = file.Substring(directoryPath.Length + 1);

            UpdateEntry(file, zipPath);
        }
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool EntryWantsUpdate(ZipEntry entry) => /*ForceFullRecompression ||*/ !entry.FileData.WantsOriginalData();

    // Called by Save(). Updates all entries requesting new data
    private void UpdateEntriesInternal(List<ZipEntry> entries, ZipSaveProgressEventArgs saveEvent, string tempPath)
    {
        Parallel.ForEach(entries, entry =>
        {
            // We assume the list passed to us has been filtered
            Debug.Assert(!entry.FileData.WantsOriginalData());

            var newFile = new FileInfo(entry.FileData.GetPath());
            var uncompressedStream = newFile.OpenRead();

            // Poll file attributes
            entry.LastFileTime = newFile.LastWriteTime;
            entry.FileAttributes = newFile.Attributes;
#if UNIX
            entry.UnixFileMode = newFile.UnixFileMode;
#endif

            entry.UncompressedSize = (int)uncompressedStream.Length;
            entry.CompressedSize = entry.UncompressedSize;

            // CRC
            {
                var crc32 = new Crc32();

                crc32.Append(uncompressedStream);

                entry.CRC32 = crc32.GetCurrentHashAsUInt32();
            }

            entry.FileData.SetTempPath(entry.FileData.GetPath());

            // Compress
            if (entry.CompressionMethod == CompressionMethod.Deflate &&
                entry.CompressionLevel != CompressionLevel.NoCompression)
            {
                // Reset position since we will have consumed the full length for CRC
                uncompressedStream.Position = 0;

                using var compressedStream = File.Create(Path.Combine(tempPath, Path.GetRandomFileName()));
                using (var deflator = new DeflateStream(compressedStream, entry.CompressionLevel, true))
                {
                    uncompressedStream.CopyTo(deflator);
                }

                // If we end up with 0 bytes, something must have gone wrong
                Debug.Assert(compressedStream.Length > 0);

                // Did we compress enough?
                if ((double)compressedStream.Length / entry.UncompressedSize <= MinimumAcceptableCompressionRatio)
                {
                    // Point to the compressed file
                    entry.FileData.SetTempPath(compressedStream.Name);

                    entry.CompressedSize = (int)compressedStream.Length;
                }
            }

            Interlocked.Add(ref saveEvent.CurrentBytesCompressed, entry.UncompressedSize);
            ZipSaveProgressChanged?.Invoke(this, saveEvent);
        });
    }

    protected virtual void Dispose(bool bIsDisposing)
    {
        if (!_bIsDisposed)
        {
            if (bIsDisposing)
            {
                _inputFile?.Dispose();
            }

            _bIsDisposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
