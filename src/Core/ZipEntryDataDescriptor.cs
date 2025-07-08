namespace Zip.Core;

// Simple abstraction over a ZipEntry's data source.

internal class ZipEntryDataDescriptor
{
    internal readonly string OriginalPath;
    internal readonly long OriginalOffset;

    private string Path;

    // @TODO in future we could use this to determine whether to re-use compression, if we save the zip multiple times
    private string TempPath = "";

    private bool bWantsOriginalData;

    #region Constructors

    internal ZipEntryDataDescriptor()
    {
        Path = OriginalPath = "";
    }

    internal ZipEntryDataDescriptor(string origPath, long origOffset)
    {
        Path = OriginalPath = origPath;
        OriginalOffset = origOffset;
        bWantsOriginalData = true;
    }

    #endregion

    internal string GetPath() => bWantsOriginalData ? OriginalPath : Path;
    internal void SetPath(string path)
    {
        Path = path;
        bWantsOriginalData = false;
    }

    internal string GetTempPath() => TempPath;
    internal void SetTempPath(string path) => TempPath = path;

    // If we have original data, our offset will be more than 0.
    internal bool HasOriginalData() => OriginalOffset != 0;
    internal bool WantsOriginalData() => bWantsOriginalData;

    internal bool HasData() => bWantsOriginalData || TempPath.Length != 0;
}
