using Microsoft.Win32.SafeHandles;
using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using static Zip.Core.ZipConstants;

namespace Zip.Core;

internal static class StreamExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe int Read<T>(this Stream stream, ref T value) where T : unmanaged
    {
        return stream.Read(new Span<byte>(Unsafe.AsPointer(ref value), sizeof(T)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static string ReadString(this Stream stream, ushort length)
    {
        if (length == 0) return "";

        var buffer = length <= 386 ? stackalloc byte[length] : new byte[length];
        stream.ReadExactly(buffer);

        return Encoding.UTF8.GetString(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static unsafe void Write<T>(this Stream stream, ref T value) where T : unmanaged
    {
        stream.Write(new Span<byte>(Unsafe.AsPointer(ref value), sizeof(T)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteString(this Stream stream, string value)
    {
        stream.Write(Encoding.UTF8.GetBytes(value));
    }

    internal static void ConstrainedCopy(this Stream src, Stream dest, int length)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        while (length > 0)
        {
            int bytesRead = src.Read(buffer, 0, Math.Min(length, buffer.Length));
            length -= bytesRead;

            dest.Write(buffer, 0, bytesRead);
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }

    internal static void ConstrainedCopy(this SafeFileHandle src, Stream dest, int length, long offset)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        while (length > 0)
        {
            int bytesRead = RandomAccess.Read(src, buffer.AsSpan(0, Math.Min(length, buffer.Length)), offset);
            length -= bytesRead;
            offset += bytesRead;

            dest.Write(buffer, 0, bytesRead);
        }

        ArrayPool<byte>.Shared.Return(buffer);
    }
}
