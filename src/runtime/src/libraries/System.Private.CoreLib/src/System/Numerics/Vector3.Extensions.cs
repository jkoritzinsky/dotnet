// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace System.Numerics
{
    public static unsafe partial class Vector
    {
        /// <summary>Reinterprets a <see cref="Vector3" /> as a new <see cref="Vector2" />.</summary>
        /// <param name="value">The vector to reinterpret.</param>
        /// <returns><paramref name="value" /> reinterpreted as a new <see cref="Vector2" />.</returns>
        public static Vector2 AsVector2(this Vector3 value) => value.AsVector128().AsVector2();

        /// <summary>Converts a <see cref="Vector3" /> to a new <see cref="Vector4" /> with the new elements zeroed.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns><paramref name="value" /> converted to a new <see cref="Vector4" /> with the new elements zeroed.</returns>
        public static Vector4 AsVector4(this Vector3 value) => value.AsVector128().AsVector4();

        /// <summary>Converts a <see cref="Vector3" /> to a new <see cref="Vector4" /> with the new elements undefined.</summary>
        /// <param name="value">The vector to convert.</param>
        /// <returns><paramref name="value" /> converted to a new <see cref="Vector4" /> with the new elements undefined.</returns>
        public static Vector4 AsVector4Unsafe(this Vector3 value) => value.AsVector128Unsafe().AsVector4();

        /// <inheritdoc cref="ExtractMostSignificantBits(Vector4)" />
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ExtractMostSignificantBits(this Vector3 vector) => vector.AsVector128().ExtractMostSignificantBits();

        /// <inheritdoc cref="GetElement(Vector4, int)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetElement(this Vector3 vector, int index)
        {
            if ((uint)index >= Vector3.ElementCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
            }
            return vector.AsVector128Unsafe().GetElement(index);
        }

        /// <summary>Stores a vector at the given destination.</summary>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination at which <paramref name="source" /> will be stored.</param>
        [CLSCompliant(false)]
        public static void Store(this Vector3 source, float* destination) => source.StoreUnsafe(ref *destination);

        /// <summary>Stores a vector at the given 8-byte aligned destination.</summary>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The aligned destination at which <paramref name="source" /> will be stored.</param>
        /// <exception cref="AccessViolationException"><paramref name="destination" /> is not 8-byte aligned.</exception>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreAligned(this Vector3 source, float* destination)
        {
            if (((nuint)destination % (uint)(Vector3.Alignment)) != 0)
            {
                ThrowHelper.ThrowAccessViolationException();
            }

            *(Vector3*)destination = source;
        }

        /// <summary>Stores a vector at the given 8-byte aligned destination.</summary>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The aligned destination at which <paramref name="source" /> will be stored.</param>
        /// <exception cref="AccessViolationException"><paramref name="destination" /> is not 8-byte aligned.</exception>
        /// <remarks>This method may bypass the cache on certain platforms.</remarks>
        [CLSCompliant(false)]
        public static void StoreAlignedNonTemporal(this Vector3 source, float* destination) => source.StoreAligned(destination);

        /// <summary>Stores a vector at the given destination.</summary>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination at which <paramref name="source" /> will be stored.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe(this Vector3 source, ref float destination)
        {
            ref byte address = ref Unsafe.As<float, byte>(ref destination);
            Unsafe.WriteUnaligned(ref address, source);
        }

        /// <summary>Stores a vector at the given destination.</summary>
        /// <param name="source">The vector that will be stored.</param>
        /// <param name="destination">The destination to which <paramref name="elementOffset" /> will be added before the vector will be stored.</param>
        /// <param name="elementOffset">The element offset from <paramref name="destination" /> from which the vector will be stored.</param>
        [CLSCompliant(false)]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void StoreUnsafe(this Vector3 source, ref float destination, nuint elementOffset)
        {
            destination = ref Unsafe.Add(ref destination, (nint)elementOffset);
            Unsafe.WriteUnaligned(ref Unsafe.As<float, byte>(ref destination), source);
        }

        /// <inheritdoc cref="ToScalar(Vector4)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToScalar(this Vector3 vector) => vector.AsVector128Unsafe().ToScalar();

        /// <inheritdoc cref="WithElement(Vector4, int, float)" />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 WithElement(this Vector3 vector, int index, float value)
        {
            if ((uint)index >= Vector3.ElementCount)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.index);
            }
            return vector.AsVector128Unsafe().WithElement(index, value).AsVector3();
        }
    }
}
