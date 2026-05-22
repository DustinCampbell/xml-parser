namespace System.Text.Xml;

/// <summary>
/// Defines options to control the behavior of <see cref="Utf8XmlWriter"/>.
/// </summary>
public struct XmlWriterOptions
{
    private int _indentSize;
    private char _indentCharacter;
    private string? _newLine;

    /// <summary>
    /// Gets or sets a value that indicates whether output should be indented.
    /// </summary>
    public bool Indented { readonly get; set; }

    /// <summary>
    /// Gets or sets the character used for indentation.
    /// </summary>
    public char IndentCharacter
    {
        readonly get => _indentCharacter == default ? ' ' : _indentCharacter;
        set => _indentCharacter = value;
    }

    /// <summary>
    /// Gets or sets the number of characters written for each indentation level.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 0.</exception>
    public int IndentSize
    {
        readonly get => _indentSize == 0 ? 2 : _indentSize;
        set
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _indentSize = value;
        }
    }

    /// <summary>
    /// Gets or sets the line terminator string to use when writing indented output.
    /// </summary>
    public string NewLine
    {
        readonly get => _newLine ?? Environment.NewLine;
        set => _newLine = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the XML declaration should be omitted.
    /// </summary>
    public bool OmitXmlDeclaration { readonly get; set; }

    /// <summary>
    /// Gets or sets the conformance level to enforce while writing.
    /// </summary>
    public XmlConformanceLevel ConformanceLevel { readonly get; set; }
}