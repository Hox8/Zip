using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Zip.Core;

internal static class CollectionsExtensions
{
    /// <summary>
    /// Get a <see cref="Span{T}"/> view over a <see cref="List{T}"/>'s data.
    /// Items should not be added or removed from the <see cref="List{T}"/> while the <see cref="Span{T}"/> is in use.
    /// </summary>
    /// <param name="value">The list to get the data view over.</param>
    /// <typeparam name="T">The type of the elements in the list.</typeparam>
    [DebuggerStepThrough, MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Span<T> AsSpan<T>(this List<T> value) => CollectionsMarshal.AsSpan(value);
}
