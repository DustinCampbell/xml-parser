namespace System.Text.Xml;

/// <summary>
/// Identifies the kind of an <see cref="XmlTrivia"/> instance.
/// </summary>
public enum XmlTriviaKind : byte
{
    /// <summary>
    /// Whitespace (spaces, tabs, newlines) between structural nodes.
    /// </summary>
    Whitespace,

    /// <summary>
    /// An XML comment (&lt;!-- ... --&gt;).
    /// </summary>
    Comment,

    /// <summary>
    /// A processing instruction (&lt;? ... ?&gt;) appearing in trivia position.
    /// </summary>
    ProcessingInstruction,
}
