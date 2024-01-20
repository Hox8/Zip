using Force.Crc32;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Zip.Core;
using Zip.Core.Events;
using Zip.Core.Exceptions;
using static Zip.Shared;

namespace Zip;

public class ZipArchive
{
    internal FileStream _buffer;
    public readonly List<ZipEntry> Entries;
    internal readonly List<ZipEntry> _entriesToUpdate = [];
    private EndOfCentralDirectory _endOfCentralDirectory;

    public const ZipVersion Version = ZipVersion.COMPRESS_Deflate;

    #region Accessors

    public string Name => _buffer.Name;

    #endregion

    #region Events

    public delegate void ProgressEventHandler(object sender, ZipProgressEventArgs e);
    public event ProgressEventHandler ProgressChanged;

    #endregion

    #region Constructors

    private ZipArchive()
    {
        Entries = [];
        _endOfCentralDirectory = new EndOfCentralDirectory { _tag = EndOfCentralDirectory.Tag };
    }

    public unsafe ZipArchive(FileStream stream)
    {
        _buffer = stream;

        // Parse end of Central Directory
        _buffer.Position = _buffer.Length - sizeof(EndOfCentralDirectory);
        _buffer.Read(ref _endOfCentralDirectory);

        // If the EOCD tag doesn't match, this probably isn't a zip archive at all
        if (_endOfCentralDirectory._tag != EndOfCentralDirectory.Tag)
            throw new InvalidZipException();

        // Parse Central Directories
        Entries = new(_endOfCentralDirectory._numEntriesTotal);
        _buffer.Position = _endOfCentralDirectory._centralDirectoryOffset;

        for (int i = 0; i < Entries.Capacity; i++)
        {
            Entries.Add(ZipEntry.ReadFromArchive(this));
        }

        CheckForUnsupportedEntries();
    }

    public static ZipArchive Read(FileStream stream) => new(stream);
    public static ZipArchive Create() => new();

    #endregion

    #region Internal methods

    // Placeholder
    private void CheckForUnsupportedEntries()
    {
        foreach (var entry in Entries)
        {
            if ((entry._data._generalPurposeFlag & GeneralPurposeFlags.Encrypted) != 0)
                throw new EncryptedEntriesException();

            if (entry._data._compressionMethod is not (CompressionMethod.Deflate or CompressionMethod.None))
                throw new UnsupportedCompressionException();

            // We aren't checking CRCs here because it is too slow--files must be decompressed in order to calculate CRCs.
            // Delegate this and other expensive checks to ZipEntry.Extract()
        }
    }

    internal bool TryGetEntry(string entryName, out ZipEntry outEntry)
    {
        entryName = SanitizePath(entryName);

        foreach (var entry in Entries)
        {
            if (entry.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase))
            {
                outEntry = entry;
                return true;
            }
        }

        outEntry = default;
        return false;
    }

    internal static long TallySizeOfEntries(ReadOnlySpan<ZipEntry> entries)
    {
        long total = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            total += entries[i].UncompressedSize;
        }

        return total;
    }

    public static string SanitizePath(string path)
    {
        // Replace any Windows slashes with Zip slashes
        path = path.Replace('\\', '/');

        // If path starts with directory separator, remove it
        if (path.StartsWith('/')) path = path[1..];

        // If path ends with directory separator, remove it
        if (path.EndsWith('/')) path = path[..^1];

        return path;
    }

    // Done in parallel. If we're compressing and CRC'ing 50 files, parallelization will speed this up greatly.
    private void UpdateEntries(SaveProgressEventArgs saveEvent)
    {
        Parallel.ForEach(_entriesToUpdate, entry =>
        {
            var uncompressed = new FileInfo(entry.PathToUpdatedContents);
            var compressed = new FileInfo($"{entry.PathToUpdatedContents}.temp");

            using (var sourceStream = uncompressed.OpenRead())
            {
                entry.UncompressedSize = (int)sourceStream.Length;

                // Calculate Crc of the uncompressed file
                entry._data._crc32 = Crc32Algorithm.Compute(sourceStream, entry.UncompressedSize);
                sourceStream.Position = 0;

                // Compress the file
                using var outputStream = compressed.Create();
                using (var compressStream = new DeflateStream(outputStream, entry.CompressionLevel, true))
                {
                    sourceStream.CopyTo(compressStream);
                }

                entry.CompressedSize = (int)outputStream.Position;
            }

            // Poll file attributes
            entry.LastFileTime = uncompressed.LastWriteTime;
            entry.FileAttributes = uncompressed.Attributes;
#if UNIX
            entry.UnixFileMode = uncompressed.UnixFileMode;
#endif

            // We can't modify the input file (that would be bad design), so:
            // - If the compression was beneficial, point the entry's path to the temp file. We'll delete it later
            // - If the compression wasn't beneficial, simply delete the temp file and use the original uncompressed
            if (entry.CompressedSize < entry.UncompressedSize)
            {
                entry.PathToUpdatedContents = compressed.FullName;
                entry.CompressionMethod = CompressionMethod.Deflate;
            }
            else
            {
                compressed.Delete();
                entry.CompressedSize = entry.UncompressedSize;
                entry.CompressionMethod = CompressionMethod.None;
            }

            Interlocked.Add(ref saveEvent._bytesCompressed, entry.UncompressedSize);
            Interlocked.Add(ref saveEvent.ProcessedBytes, entry.UncompressedSize);
            ProgressChanged?.Invoke(this, saveEvent);
        });
    }

    #endregion

    public void Save(string path)
    {
        long totalBytesToWrite = 0;
        long totalBytesToCompress = 0;

        _buffer ??= File.Create(path);

        foreach (var entry in Entries)
        {
            if (entry.WantsUpdate)
            {
                // We don't know what the compressed size will be ahead of time, so just use the uncompressed size
                long uncompressedSize = new FileInfo(entry.PathToUpdatedContents).Length;
                totalBytesToCompress += uncompressedSize;
                totalBytesToWrite += uncompressedSize;
            }
            else
            {
                // We'll reuse the entry's existing compression if it doesn't need updating
                totalBytesToWrite += entry.CompressedSize;
            }
        }

        // A central directory record needs to be written for every entry present, which represents a small but quantifiable unit of work.
        // We approximate this workload ahead of time by making it attribute to 1% of the overall progress (affected by weighting, so this may end up being smaller than 1%)
        int onePercentOfTotal = (int)(totalBytesToWrite / 100f);

        SaveProgressEventArgs saveEvent = new(totalBytesToWrite + onePercentOfTotal, totalBytesToCompress, 0);

        // Compress and calculate CRCs for all entries queued for update
        UpdateEntries(saveEvent);

        string tempPath = $"{path}.temp";
        using (var outputStream = File.Create(tempPath))
        {
            // Write out entry headers + data
            foreach (var entry in Entries)
            {
                // Position source buffer to start of data (after LFH)
                _buffer.Position = entry._data._relativeOffsetOfLocalHeader;
                ZipEntry.ReadAndSkipOverLocalFileHeader(_buffer);

                // @TODO this is gross
                entry.PrepareForSave();

                // Update entry offset and write out its header
                entry._data._relativeOffsetOfLocalHeader = (int)outputStream.Position;
                entry.WriteLocalHeader(outputStream);

                if (entry.WantsUpdate)
                {
                    // Copy data from new source to destination
                    var newSource = new FileInfo(entry.PathToUpdatedContents);

                    using (var newSourceReader = newSource.OpenRead())
                    {
                        newSourceReader.CopyTo(outputStream);
                    }

                    // If newSource is a temp compressed file, delete it now that we've finished using it
                    if (entry.CompressionMethod is not CompressionMethod.None)
                    {
                        newSource.Delete();
                    }
                }
                else
                {
                    // Copy old compressed data from source zip stream to destination
                    _buffer.ConstrainedCopy(outputStream, entry.CompressedSize);
                }

                // Progress event
                long entryBytes = entry.WantsUpdate ? entry.UncompressedSize : entry.CompressedSize;
                saveEvent._bytesWritten += entryBytes;
                saveEvent.ProcessedBytes += entryBytes;
                ProgressChanged?.Invoke(this, saveEvent);
            }

            // Write out central directories

            _endOfCentralDirectory._centralDirectoryOffset = (int)outputStream.Position;

            foreach (var entry in Entries)
            {
                outputStream.Write(entry._data);
                outputStream.WriteString(entry.Name);

                if (entry.Comment is not null)
                {
                    outputStream.WriteString(entry.Comment);
                }
            }

            // Write end of central directory
            _endOfCentralDirectory._centralDirectorySize = (int)outputStream.Position - _endOfCentralDirectory._centralDirectoryOffset;
            _endOfCentralDirectory._numEntriesTotal = (short)Entries.Count;
            _endOfCentralDirectory._numEntriesOnDisk = _endOfCentralDirectory._numEntriesTotal;
            outputStream.Write(_endOfCentralDirectory);
        }

        // Let saveEvent know we're finished
        saveEvent._bytesWritten += onePercentOfTotal;
        saveEvent.ProcessedBytes += onePercentOfTotal;
        ProgressChanged?.Invoke(this, saveEvent);

        _buffer?.Dispose();
        File.Move(tempPath, path, true);
    }

    public ZipEntry? GetEntry(string entryName)
    {
        TryGetEntry(entryName, out ZipEntry entry);

        return entry;
    }

    public void RemoveEntry(string entryName)
    {
        TryGetEntry(entryName, out ZipEntry entry);

        if (entry is not null)
        {
            Entries.Remove(entry);
        }
    }

    public void ExtractEntries(ZipEntry[] entries, string basePath)
    {
        ReadOnlySpan<ZipEntry> span = entries.AsSpan();
        var extractArgs = new ExtractProgressEventArgs(TallySizeOfEntries(span), 0);

        foreach (var entry in span)
        {
            entry.Extract(basePath);
            extractArgs.ProcessedBytes += entry.UncompressedSize;

            ProgressChanged?.Invoke(this, extractArgs);
        }
    }

    public void ExtractEntries(List<ZipEntry> entries, string basePath)
    {
        ReadOnlySpan<ZipEntry> span = CollectionsMarshal.AsSpan(entries);
        var extractArgs = new ExtractProgressEventArgs(TallySizeOfEntries(span), 0);

        foreach (var entry in span)
        {
            entry.Extract(basePath);
            extractArgs.ProcessedBytes += entry.UncompressedSize;

            ProgressChanged?.Invoke(this, extractArgs);
        }
    }

    public void ExtractEntriesParallel(ZipEntry[] entries, string basePath)
    {
        var extractArgs = new ExtractProgressEventArgs(TallySizeOfEntries(entries), 0);

        Parallel.ForEach(entries, entry =>
        {
            entry.Extract(basePath);

            Interlocked.Add(ref extractArgs.ProcessedBytes, entry.UncompressedSize);
            ProgressChanged?.Invoke(this, extractArgs);
        });
    }
    public void ExtractEntriesParallel(List<ZipEntry> entries, string basePath)
    {
        var extractArgs = new ExtractProgressEventArgs(TallySizeOfEntries(CollectionsMarshal.AsSpan(entries)), 0);

        Parallel.ForEach(entries, entry =>
        {
            entry.Extract(basePath);

            Interlocked.Add(ref extractArgs.ProcessedBytes, entry.UncompressedSize);
            ProgressChanged?.Invoke(this, extractArgs);
        });
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="diskPath">Disk path of the file to update the zip with.</param>
    /// <param name="zipPath">The path inside the zip where the file will go.</param>
    public void UpdateEntry(string diskPath, string zipPath)
    {
        if (!TryGetEntry(zipPath, out ZipEntry entry))
        {
            entry = ZipEntry.CreateNew(this, zipPath);
            Entries.Add(entry);
        }

        entry.Update(diskPath);
    }

    public void UpdateEntries(ZipEntry[] entries, string basePath = "")
    {
        foreach (var entry in entries)
        {
            entry.Update(basePath);
        }
    }

    public void UpdateEntries(List<ZipEntry> entries, string basePath = "")
    {
        foreach (var entry in entries)
        {
            entry.Update(basePath);
        }
    }

    public void UpdateEntries(string directoryPath, string basePath = "")
    {
        int skipLength = Path.GetFullPath(directoryPath).Length + 1;

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories))
        {
            string zipPath = Path.Combine(basePath, file[skipLength..]).Replace('\\', '/');
            UpdateEntry(file, zipPath);
        }
    }
}
