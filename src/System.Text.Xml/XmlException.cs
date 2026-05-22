namespace System.Text.Xml;

/// <summary>
/// Defines a custom exception object that is thrown when invalid XML text is encountered.
/// </summary>
public sealed class XmlException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="XmlException"/> class.
    /// </summary>
    public XmlException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlException"/> class with a custom error message.
    /// </summary>
    public XmlException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlException"/> class with a specified message and inner exception.
    /// </summary>
    public XmlException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="XmlException"/> class with path and position information.
    /// </summary>
    public XmlException(string? message, string? path, long? lineNumber, long? bytePositionInLine, Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
        LineNumber = lineNumber;
        BytePositionInLine = bytePositionInLine;
    }

    /// <summary>
    /// Gets the line number where the exception occurred.
    /// </summary>
    public long? LineNumber { get; }

    /// <summary>
    /// Gets the byte position within the current line where the exception occurred.
    /// </summary>
    public long? BytePositionInLine { get; }

    /// <summary>
    /// Gets the logical XML path associated with the exception, if available.
    /// </summary>
    public string? Path { get; }
}