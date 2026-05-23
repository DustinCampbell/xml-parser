namespace System.Text.Xml;

/// <summary>
/// Options controlling how <see cref="XmlDocument"/> parses XML.
/// </summary>
public sealed class XmlDocumentOptions
{
    /// <summary>
    /// Gets or sets whether trivia (whitespace and comments) is preserved during parsing.
    /// When true, the document retains whitespace and comment positions and they can be
    /// accessed via <see cref="XmlElement.GetLeadingTrivia"/> and <see cref="XmlElement.GetTrailingTrivia"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This follows the Roslyn trivia model: whitespace and comments that appear between structural
    /// nodes are stored as "trivia" attached to adjacent nodes, rather than as child nodes.
    /// This keeps the structural DOM clean while enabling high-fidelity round-tripping.
    /// </para>
    /// <para>
    /// When false (default), insignificant whitespace is discarded and comments are stored as
    /// regular child nodes.
    /// </para>
    /// </remarks>
    public bool PreserveTrivia { get; set; }

    /// <summary>
    /// Gets a default options instance with trivia preservation disabled.
    /// </summary>
    public static XmlDocumentOptions Default { get; } = new XmlDocumentOptions();
}
