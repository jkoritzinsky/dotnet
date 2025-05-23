// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Microsoft.Extensions.DependencyInjection.ServiceLookup
{
    internal static class ThrowHelper
    {
        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowObjectDisposedException()
        {
            throw new ObjectDisposedException(nameof(IServiceProvider));
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowInvalidOperationException_KeyedServiceAnyKeyUsedToResolveService()
        {
            throw new InvalidOperationException(SR.Format(SR.KeyedServiceAnyKeyUsedToResolveService, nameof(IServiceProvider), nameof(IServiceScopeFactory)));
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowInvalidOperationException_NoServiceRegistered(Type serviceType)
        {
            throw new InvalidOperationException(SR.Format(SR.NoServiceRegistered, serviceType));
        }

        [DoesNotReturn]
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ThrowInvalidOperationException_NoKeyedServiceRegistered(Type serviceType, Type keyType)
        {
            throw new InvalidOperationException(SR.Format(SR.NoKeyedServiceRegistered, serviceType, keyType));
        }
    }
}
