// The MIT License (MIT)
//
// Copyright (c) 2016 force
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.IO;
using System.Security.Cryptography;

namespace Force.Crc32;

/// <summary>
/// Implementation of CRC-32.
/// This class supports several convenient static methods returning the CRC as UInt32.
/// </summary>
public class Crc32Algorithm : HashAlgorithm
{
    private uint _currentCrc;
    private readonly bool _isBigEndian = true;
    private static readonly SafeProxy _proxy = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="Crc32Algorithm"/> class. 
    /// </summary>
    public Crc32Algorithm()
    {
        HashSizeValue = 32;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Crc32Algorithm"/> class. 
    /// </summary>
    /// <param name="isBigEndian">Should return bytes result as big endian or little endian</param>
    // Crc32 by dariogriffo uses big endian, so, we need to be compatible and return big endian as default
    public Crc32Algorithm(bool isBigEndian = true) : this()
    {
        _isBigEndian = isBigEndian;
    }

    /// <summary>
    /// Computes CRC-32 from multiple buffers.
    /// Call this method multiple times to chain multiple buffers.
    /// </summary>
    /// <param name="initial">
    /// Initial CRC value for the algorithm. It is zero for the first buffer.
    /// Subsequent buffers should have their initial value set to CRC value returned by previous call to this method.
    /// </param>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <param name="offset">Offset of the input data within the buffer.</param>
    /// <param name="length">Length of the input data in the buffer.</param>
    /// <returns>Accumulated CRC-32 of all buffers processed so far.</returns>
    public static uint Append(uint initial, byte[] input, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(offset, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + length, input.Length);

        return AppendInternal(initial, input, offset, length);
    }

    /// <summary>
    /// Computes CRC-32 from multiple buffers.
    /// Call this method multiple times to chain multiple buffers.
    /// </summary>
    /// <param name="initial">
    /// Initial CRC value for the algorithm. It is zero for the first buffer.
    /// Subsequent buffers should have their initial value set to CRC value returned by previous call to this method.
    /// </param>
    /// <param name="input">Input buffer containing data to be checksummed.</param>
    /// <returns>Accumulated CRC-32 of all buffers processed so far.</returns>
    public static uint Append(uint initial, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return AppendInternal(initial, input, 0, input.Length);
    }

    public static uint Append(uint initial, FileStream input, int length)
    {
        ArgumentNullException.ThrowIfNull(input);
        // ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(input.Position + length, input.Length);

        return AppendInternal(initial, input, length);
    }

    /// <summary>
    /// Computes CRC-32 from input buffer.
    /// </summary>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <param name="offset">Offset of the input data within the buffer.</param>
    /// <param name="length">Length of the input data in the buffer.</param>
    /// <returns>CRC-32 of the data in the buffer.</returns>
    public static uint Compute(byte[] input, int offset, int length) => Append(0, input, offset, length);

    /// <summary>
    /// Computes CRC-32 from input buffer.
    /// </summary>
    /// <param name="input">Input buffer containing data to be checksummed.</param>
    /// <returns>CRC-32 of the buffer.</returns>
    public static uint Compute(byte[] input) => Append(0, input);

    /// <summary>
    /// Computes CRC-32 from input buffer.
    /// </summary>
    /// <param name="input">Input buffer containing data to be checksummed.</param>
    /// <param name="length">Length of the input data in the buffer.</param>
    /// <returns>CRC-32 of the buffer.</returns>
    public static uint Compute(FileStream input, int length) => Append(0, input, length);

    /// <summary>
    /// Computes CRC-32 from input buffer and writes it after end of data (buffer should have 4 bytes reserved space for it). Can be used in conjunction with <see cref="IsValidWithCrcAtEnd(byte[],int,int)"/>
    /// </summary>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <param name="offset">Offset of the input data within the buffer.</param>
    /// <param name="length">Length of the input data in the buffer.</param>
    /// <returns>CRC-32 of the data in the buffer.</returns>
    public static uint ComputeAndWriteToEnd(byte[] input, int offset, int length)
    {
        // Length of data should be less than array length - 4 bytes of CRC data
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length + 4, input.Length);

        var crc = Append(0, input, offset, length);
        var r = offset + length;

        input[r] = (byte)crc;
        input[r + 1] = (byte)(crc >> 8);
        input[r + 2] = (byte)(crc >> 16);
        input[r + 3] = (byte)(crc >> 24);

        return crc;
    }

    /// <summary>
    /// Computes CRC-32 from input buffer - 4 bytes and writes it as last 4 bytes of buffer. Can be used in conjunction with <see cref="IsValidWithCrcAtEnd(byte[])"/>
    /// </summary>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <returns>CRC-32 of the data in the buffer.</returns>
    public static uint ComputeAndWriteToEnd(byte[] input)
    {
        // Input array should be 4 bytes at least
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Length, 4);

        return ComputeAndWriteToEnd(input, 0, input.Length - 4);
    }

    /// <summary>
    /// Validates correctness of CRC-32 data in source buffer with assumption that CRC-32 data located at end of buffer in reverse bytes order. Can be used in conjunction with <see cref="ComputeAndWriteToEnd(byte[],int,int)"/>
    /// </summary>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <param name="offset">Offset of the input data within the buffer.</param>
    /// <param name="lengthWithCrc">Length of the input data in the buffer with CRC-32 bytes.</param>
    /// <returns>Is checksum valid.</returns>
    public static bool IsValidWithCrcAtEnd(byte[] input, int offset, int lengthWithCrc)
    {
        return Append(0, input, offset, lengthWithCrc) == 0x2144DF1C;
    }

    /// <summary>
    /// Validates correctness of CRC-32 data in source buffer with assumption that CRC-32 data located at end of buffer in reverse bytes order. Can be used in conjunction with <see cref="ComputeAndWriteToEnd(byte[],int,int)"/>
    /// </summary>
    /// <param name="input">Input buffer with data to be checksummed.</param>
    /// <returns>Is checksum valid.</returns>
    public static bool IsValidWithCrcAtEnd(byte[] input)
    {
        // Input array should be 4 bytes at least
        ArgumentOutOfRangeException.ThrowIfLessThan(input.Length, 4);

        return Append(0, input, 0, input.Length) == 0x2144DF1C;
    }

    /// <summary>
    /// Resets internal state of the algorithm. Used internally.
    /// </summary>
    public override void Initialize()
    {
        _currentCrc = 0;
    }

    /// <summary>
    /// Appends CRC-32 from given buffer
    /// </summary>
    protected override void HashCore(byte[] input, int offset, int length)
    {
        _currentCrc = AppendInternal(_currentCrc, input, offset, length);
    }

    /// <summary>
    /// Computes CRC-32 from <see cref="HashCore"/>
    /// </summary>
    protected override byte[] HashFinal()
    {
        return _isBigEndian
            ? [(byte)(_currentCrc >> 24), (byte)(_currentCrc >> 16), (byte)(_currentCrc >> 8), (byte)_currentCrc]
            : [(byte)_currentCrc, (byte)(_currentCrc >> 8), (byte)(_currentCrc >> 16), (byte)(_currentCrc >> 24)];
    }

    private static uint AppendInternal(uint initial, byte[] input, int offset, int length)
    {
        return length > 0
            ? _proxy.Append(initial, input, offset, length)
            : initial;
    }

    private static uint AppendInternal(uint initial, FileStream input, int length)
    {
        return length > 0
            ? _proxy.Append(initial, input, length)
            : initial;
    }
}
