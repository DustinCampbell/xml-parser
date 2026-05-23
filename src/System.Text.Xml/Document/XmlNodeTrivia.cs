namespace System.Text.Xml;

/// <summary>
/// Represents a trivia item (whitespace, comment, or processing instruction) attached
/// to a mutable <see cref="XmlNode"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="XmlTrivia"/> (which references byte offsets in the read-only document),
/// <see cref="XmlNodeTrivia"/> owns its text content as a string, suitable for mutable DOM operations.
/// </para>
/// </remarks>
public readonly struct XmlNodeTrivia
{
    /// <summary>
    /// Initializes a new <see cref="XmlNodeTrivia"/> instance.
    /// </summary>
    /// <param name="kind">The kind of trivia.</param>
    /// <param name="text">The text content of the trivia.</param>
    public XmlNodeTrivia(XmlTriviaKind kind, string text)
    {
        Kind = kind;
        Text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <summary>
    /// Gets the kind of this trivia.
    /// </summary>
    public XmlTriviaKind Kind { get; }

    /// <summary>
    /// Gets the text content of this trivia.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Creates a whitespace trivia item.
    /// </summary>
    public static XmlNodeTrivia Whitespace(string text) => new(XmlTriviaKind.Whitespace, text);

    /// <summary>
    /// Creates a comment trivia item.
    /// </summary>
    public static XmlNodeTrivia Comment(string text) => new(XmlTriviaKind.Comment, text);

    /// <summary>
    /// Creates a processing instruction trivia item.
    /// </summary>
    public static XmlNodeTrivia ProcessingInstruction(string text) => new(XmlTriviaKind.ProcessingInstruction, text);
}
