using System.Diagnostics.CodeAnalysis;

namespace System.Text.Xml;

internal static class ThrowHelper
{
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