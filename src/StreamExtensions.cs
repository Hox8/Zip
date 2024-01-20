using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Zip
{
    public static class StreamExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int Read<T>(this Stream stream, ref T value) where T : unmanaged
        {
            fixed (void* pValue = &value)
            {
                return stream.Read(new Span<byte>(pValue, sizeof(T)));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string ReadString(this Stream stream, ushort length)
        {
            if (length == 0) return "";

            var buffer = length <= 386 ? stackalloc byte[length] : new byte[length];
            stream.ReadExactly(buffer);

            return Encoding.UTF8.GetString(buffer);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Write<T>(this Stream stream, in T value) where T : unmanaged
        {
            fixed (void* pValue = &value)
            {
                stream.Write(new Span<byte>(pValue, sizeof(T)));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteString(this Stream stream, string value)
        {
            stream.Write(Encoding.UTF8.GetBytes(value));
        }

        public static void ConstrainedCopy(this Stream src, Stream dest, int length)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(src.Position + length, src.Length);

            byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);

            int bytesRead;
            while (length > 0)
            {
                bytesRead = src.Read(buffer, 0, Math.Min(length, buffer.Length));
                length -= bytesRead;

                dest.Write(buffer, 0, bytesRead);
            }

            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
