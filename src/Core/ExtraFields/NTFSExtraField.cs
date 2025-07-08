#if WITH_EXTRAFIELDS

using System;
using System.Diagnostics;
using System.IO;

namespace Zip.Core.ExtraFields;

// Quick and dirty implementation
internal sealed class NTFSExtraField : ExtraFieldBase
{
    private DateTime FileLastModificationTime;
    private DateTime FileLastAccessTime;
    private DateTime FileCreationTime;

    public unsafe NTFSExtraField(Stream stream, ZipEntry entry)
    {
        int reserved = default;
        stream.Read(ref reserved);

        ushort numAttributes = default;
        stream.Read(ref numAttributes);
        Debug.Assert(numAttributes == 1);

        ushort attributeSize = default;
        stream.Read(ref attributeSize);
        Debug.Assert(attributeSize == sizeof(DateTime) * 3);

        long temp = default;

        stream.Read(ref temp);
        FileLastModificationTime = DateTime.FromFileTime(temp);

        stream.Read(ref temp);
        FileLastAccessTime = DateTime.FromFileTime(temp);

        stream.Read(ref temp);
        FileCreationTime = DateTime.FromFileTime(temp);
    }

    public override void Write(Stream stream)
    {
        throw new NotImplementedException();
    }
}

#endif // WITH_EXTRAFIELDS
