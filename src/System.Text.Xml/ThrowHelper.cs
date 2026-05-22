using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Text.Xml;

internal static class ThrowHelper
{
    public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

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

    public static void ThrowIfDisposed(bool condition, object instance)
    {
        if (condition)
        {
            throw new ObjectDisposedException(instance.GetType().FullName);
        }
    }

    [DoesNotReturn]
    public static void ThrowXmlException(string message, long lineNumber, long bytePositionInLine, string? path = null)
        => throw new XmlException(message, path, lineNumber, bytePositionInLine);

    [DoesNotReturn]
    public static void ThrowUnexpectedEndOfFile(long lineNumber, long bytePositionInLine, string? path = null)
        => throw new XmlException("Unexpected end of XML input.", path, lineNumber, bytePositionInLine);

    [DoesNotReturn]
    public static void ThrowExpectedToken(string expected, long lineNumber, long bytePositionInLine, string? path = null)
        => throw new XmlException($"Expected {expected}.", path, lineNumber, bytePositionInLine);

    [DoesNotReturn]
    public static void ThrowInvalidXmlName(string name)
        => throw new ArgumentException($"'{name}' is not a valid XML name.", nameof(name));

    [DoesNotReturn]
    public static void ThrowInvalidOperation(string message)
        => throw new InvalidOperationException(message);
}