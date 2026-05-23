namespace System.Text.Xml;

/// <summary>
/// Represents a piece of trivia (whitespace, comment, or processing instruction)
/// associated with a node in an <see cref="XmlDocument"/>.
/// </summary>
/// <remarks>
/// <para>
/// This follows the Roslyn trivia model: each structural node can have leading trivia
/// (appearing before it in the source) and trailing trivia (appearing after it).
/// Trivia enables high-fidelity round-tripping of XML documents while keeping the
/// structural DOM clean.
/// </para>
/// </remarks>
public readonly struct XmlTrivia
{
    private readonly XmlDocument _document;
    private readonly int _start;
    private readonly int _length;
    private readonly XmlTriviaKind _kind;

    internal XmlTrivia(XmlDocument document, XmlTriviaKind kind, int start, int length)
    {
        _document = document;
        _start = start;
        _length = length;
        _kind = kind;
    }

    /// <summary>
    /// Gets the kind of this trivia.
    /// </summary>
    public XmlTriviaKind Kind => _kind;

    /// <summary>
    /// Gets the text content of this trivia, decoded from the source UTF-8 bytes.
    /// </summary>
    public string Text => _document.GetString(_start, _length);

    /// <summary>
    /// Gets the raw UTF-8 byte span of this trivia within the document source.
    /// </summary>
    internal ReadOnlySpan<byte> Span => _document.GetSourceSpan(_start, _length);

    /// <summary>
    /// Gets the length of this trivia in bytes.
    /// </summary>
    public int Length => _length;
}
