namespace System.Text.Xml;

/// <summary>
/// Defines options to control the behavior of <see cref="Utf8XmlReader"/>.
/// </summary>
public struct XmlReaderOptions
{
    private const int DefaultMaxDepth = 64;
    private int _maxDepth;

    /// <summary>
    /// Gets or sets the maximum depth allowed while reading nested elements.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int MaxDepth
    {
        readonly get => _maxDepth == 0 ? DefaultMaxDepth : _maxDepth;
        set
        {
            if (value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _maxDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets how comment nodes are handled.
    /// </summary>
    public XmlCommentHandling CommentHandling { readonly get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether insignificant whitespace is ignored.
    /// </summary>
    public bool IgnoreWhitespace { readonly get; set; }

    /// <summary>
    /// Gets or sets the conformance level to enforce while reading.
    /// </summary>
    public XmlConformanceLevel ConformanceLevel { readonly get; set; }
}