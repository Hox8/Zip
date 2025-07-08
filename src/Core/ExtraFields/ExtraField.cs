#if WITH_EXTRAFIELDS

using System.Diagnostics;
using System.IO;

namespace Zip.Core.ExtraFields;

internal enum ExtraFieldType : ushort
{
    Zip64ExtendedInformation = 0x0001,
    NTFSExtraField = 0x000a,
    OpenVMSExtraField = 0x000c,
    UnixExtraField = 0x000d,
    PatchDescriptor = 0x000f,
    StrongEncryption = 0x0017,
    MVSExtraField = 0x0065
}

// Child classes should override the constructor and Write methods
internal abstract class ExtraFieldBase
{
    public abstract void Write(Stream stream);

    public static ExtraFieldBase Read(Stream stream, ZipEntry entry)
    {
        ExtraFieldType type = default;
        stream.Read(ref type);

        ushort extraFieldLength = default;
        stream.Read(ref extraFieldLength);

        int pos = (int)stream.Position;

        ExtraFieldBase field = type switch
        {
            ExtraFieldType.NTFSExtraField => new NTFSExtraField(stream, entry),
            _ => new EmptyField(stream, entry, type),
        };

        // Ensure we've read the correct number of bytes
        Debug.Assert(stream.Position - pos == extraFieldLength);

        return field;
    }
}

// Generic/"none" extra field type
internal sealed class EmptyField : ExtraFieldBase
{
    public EmptyField()
    {
    }

    public EmptyField(Stream stream, ZipEntry entry, ExtraFieldType type)
    {
        Debug.Write($"Skipping unsupported ExtraField '{type} ({(ushort)type:X})'");

        // Skip over any remaining data, minus the ushort tag + size we've already read
        stream.Position += entry.ExtraFieldLength - sizeof(int);
    }

    public override void Write(Stream stream)
    {
        // Generic implementation does nothing
    }
}

#endif // WITH_EXTRAFIELDS
