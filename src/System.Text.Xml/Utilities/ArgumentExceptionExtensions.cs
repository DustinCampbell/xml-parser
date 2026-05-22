// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Text.Xml;

internal static class ArgumentExceptionPolyfills
{
    public static class ArgumentNullExceptionEx
    {
        public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (argument is null)
            {
                throw new ArgumentNullException(paramName);
            }
        }
    }

    public static class ArgumentExceptionEx
    {
        public static void ThrowIfNullOrEmpty(string? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        {
            if (string.IsNullOrEmpty(argument))
            {
                if (argument is null)
                {
                    throw new ArgumentNullException(paramName);
                }

                throw new ArgumentException("The value cannot be an empty string.", paramName);
            }
        }
    }
}
#endif
