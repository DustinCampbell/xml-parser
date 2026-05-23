using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System.Text.Xml;

/// <summary>
/// Represents a parsed, read-only XML document backed by flat metadata for high-performance navigation.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="XmlDocument"/> follows the same design as <c>System.Text.Json.JsonDocument</c>:
/// parsing produces a read-only document that can be navigated with zero-allocation struct views
/// (<see cref="XmlElement"/>, <see cref="XmlAttribute"/>).
/// </para>
/// <para>
/// The document retains the original UTF-8 bytes and a flat metadata database. All string
/// properties are decoded lazily from the source bytes on access.
/// </para>
/// </remarks>
public sealed class XmlDocument : IDisposable
{
    private static readonly Encoding s_utf8 = new UTF8Encoding(false, true);

    private readonly byte[] _utf8Source;
    private readonly MetadataDb _db;
    private readonly TriviaDb? _triviaDb;
    private readonly int _rootIndex;
    private readonly int _declarationIndex; // -1 if no declaration
    private Utf8StringCache? _nameCache; // lazily created for name deduplication
    private bool _disposed;

    internal XmlDocument(byte[] utf8Source, MetadataDb db, int rootIndex, int declarationIndex, TriviaDb? triviaDb = null)
    {
        _utf8Source = utf8Source;
        _db = db;
        _rootIndex = rootIndex;
        _declarationIndex = declarationIndex;
        _triviaDb = triviaDb;
    }

    /// <summary>
    /// Gets whether this document preserves trivia (whitespace and comments).
    /// </summary>
    public bool HasTrivia => _triviaDb is not null;

    /// <summary>
    /// Gets the root element of the document.
    /// </summary>
    public XmlElement RootElement => new XmlElement(this, _rootIndex);

    // Keep Root as an alias for backward compatibility with tests/benchmarks
    /// <summary>
    /// Gets the root element of the document.
    /// </summary>
    public XmlElement Root => RootElement;

    /// <summary>
    /// Gets whether the document has an XML declaration.
    /// </summary>
    public bool HasDeclaration => _declarationIndex >= 0;

    /// <summary>
    /// Gets the XML declaration version, or null if no declaration.
    /// </summary>
    public string? DeclarationVersion => HasDeclaration ? GetDeclarationAttribute("version") : null;

    /// <summary>
    /// Gets the XML declaration encoding, or null if no declaration.
    /// </summary>
    public string? DeclarationEncoding => HasDeclaration ? GetDeclarationAttribute("encoding") : null;

    /// <summary>
    /// Parses an XML document from a UTF-16 string.
    /// </summary>
    public static XmlDocument Parse(string xml)
    {
        ThrowHelper.ThrowIfNull(xml);
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(xml);
        return XmlMetadataParser.Parse(utf8Bytes, preserveTrivia: false);
    }

    /// <summary>
    /// Parses an XML document from a UTF-16 string with options.
    /// </summary>
    public static XmlDocument Parse(string xml, XmlDocumentOptions options)
    {
        ThrowHelper.ThrowIfNull(xml);
        ThrowHelper.ThrowIfNull(options);
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(xml);
        return XmlMetadataParser.Parse(utf8Bytes, preserveTrivia: options.PreserveTrivia);
    }

    /// <summary>
    /// Parses an XML document from a UTF-8 byte array.
    /// </summary>
    public static XmlDocument Parse(byte[] utf8Xml)
    {
        ThrowHelper.ThrowIfNull(utf8Xml);
        return XmlMetadataParser.Parse(utf8Xml, preserveTrivia: false);
    }

    /// <summary>
    /// Parses an XML document from a UTF-8 byte array with options.
    /// </summary>
    public static XmlDocument Parse(byte[] utf8Xml, XmlDocumentOptions options)
    {
        ThrowHelper.ThrowIfNull(utf8Xml);
        ThrowHelper.ThrowIfNull(options);
        return XmlMetadataParser.Parse(utf8Xml, preserveTrivia: options.PreserveTrivia);
    }

    /// <summary>
    /// Parses an XML document from a UTF-8 byte span.
    /// </summary>
    public static XmlDocument Parse(ReadOnlySpan<byte> utf8Xml)
    {
        return XmlMetadataParser.Parse(utf8Xml.ToArray(), preserveTrivia: false);
    }

    /// <summary>
    /// Parses an XML document asynchronously from a stream.
    /// </summary>
    public static async Task<XmlDocument> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ThrowHelper.ThrowIfNull(stream);
        var buffer = await ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
        return Parse(buffer);
    }

    /// <summary>
    /// Loads an XML document from a file.
    /// </summary>
    public static XmlDocument Load(string filePath)
    {
        ThrowHelper.ThrowIfNullOrEmpty(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>
    /// Loads an XML document from a stream.
    /// </summary>
    public static XmlDocument Load(Stream stream)
    {
        ThrowHelper.ThrowIfNull(stream);
        return Parse(ReadAllBytes(stream));
    }

    /// <summary>
    /// Saves the document to the specified stream.
    /// </summary>
    public void Save(Stream stream)
    {
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNull(stream);

        using var textWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true);
        WriteToTextWriter(textWriter);
        textWriter.Flush();
    }

    /// <summary>
    /// Saves the document to a file.
    /// </summary>
    public void Save(string filePath)
    {
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNullOrEmpty(filePath);

        using var stream = File.Create(filePath);
        Save(stream);
    }

    /// <summary>
    /// Writes the document to the specified UTF-8 XML writer.
    /// </summary>
    public void WriteTo(Utf8XmlWriter writer)
    {
        ThrowHelper.ThrowIfDisposed(_disposed, this);
        ThrowHelper.ThrowIfNull(writer);

        if (HasDeclaration)
        {
            writer.WriteStartDocument();
        }

        WriteElementTo(writer, _rootIndex);
    }

    /// <summary>
    /// Returns the serialized XML string.
    /// </summary>
    public override string ToString()
    {
        using var stream = new MemoryStream();
        Save(stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, checked((int)stream.Length));
    }

    /// <summary>
    /// Releases resources held by the document.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
    }

    // --- Internal helpers used by XmlElement/XmlAttribute/XmlNodeValue ---

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly DbRow GetRow(int index) => ref _db.GetRow(index);

    /// <summary>
    /// Decodes a name from UTF-8 source with deduplication via the string cache.
    /// Names repeat frequently in XML (element names, attribute names, prefixes, namespace URIs).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetName(int start, int length)
    {
        if (length == 0) return string.Empty;
        var cache = _nameCache ??= new Utf8StringCache(32);
        return cache.GetOrAdd(_utf8Source.AsSpan(start, length));
    }

    /// <summary>
    /// Decodes UTF-8 bytes from the source at the given offset/length (no caching).
    /// Used for values that are unlikely to repeat.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal string GetString(int start, int length)
    {
        if (length == 0) return string.Empty;
#if NET
        return s_utf8.GetString(_utf8Source.AsSpan(start, length));
#else
        return s_utf8.GetString(_utf8Source, start, length);
#endif
    }

    /// <summary>
    /// Decodes UTF-8 bytes and unescapes XML character entities.
    /// </summary>
    internal string GetDecodedValue(int start, int length)
    {
        if (length == 0) return string.Empty;
        var span = _utf8Source.AsSpan(start, length);
        string text = GetString(start, length);
        // Only unescape if there's an ampersand
        if (span.IndexOf((byte)'&') >= 0)
        {
            return Utf8XmlReader.Unescape(text);
        }
        return text;
    }

    /// <summary>
    /// Checks if the UTF-8 bytes at the given location equal the specified string.
    /// </summary>
    internal bool NameEquals(int start, int length, string value)
    {
        if (length != value.Length) return false;
        if (length == 0) return true;
        var span = _utf8Source.AsSpan(start, length);
        // Fast path for ASCII names (most XML names are ASCII)
        for (int i = 0; i < length; i++)
        {
            if (span[i] != (byte)value[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Checks if the namespace URI bytes equal the specified string.
    /// </summary>
    internal bool NamespaceEquals(int start, int length, string value)
    {
        return NameEquals(start, length, value);
    }

    /// <summary>
    /// Gets a span of the raw UTF-8 source bytes at the given offset/length.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ReadOnlySpan<byte> GetSourceSpan(int start, int length)
    {
        return _utf8Source.AsSpan(start, length);
    }

    /// <summary>
    /// Gets the trivia entry at the specified index in the trivia database.
    /// </summary>
    internal XmlTrivia GetTrivia(int index)
    {
        if (_triviaDb is null) throw new InvalidOperationException("Document was not parsed with PreserveTrivia.");
        ref readonly TriviaEntry entry = ref _triviaDb.GetEntry(index);
        return new XmlTrivia(this, entry.Kind, entry.Start, entry.Length);
    }

    /// <summary>
    /// Gets the leading trivia list for the node at the given index.
    /// </summary>
    internal XmlTriviaList GetLeadingTriviaForNode(int nodeIndex)
    {
        if (_triviaDb is null) return default;
        _triviaDb.GetLeadingTrivia(nodeIndex, out int start, out int count);
        if (count == 0) return default;
        return new XmlTriviaList(this, start, count);
    }

    /// <summary>
    /// Gets the trailing trivia list for the node at the given index.
    /// </summary>
    internal XmlTriviaList GetTrailingTriviaForNode(int nodeIndex)
    {
        if (_triviaDb is null) return default;
        _triviaDb.GetTrailingTrivia(nodeIndex, out int start, out int count);
        if (count == 0) return default;
        return new XmlTriviaList(this, start, count);
    }

    // --- Private helpers ---

    private string? GetDeclarationAttribute(string name)
    {
        if (_declarationIndex < 0) return null;
        ref readonly DbRow row = ref _db.GetRow(_declarationIndex);
        string text = GetString(row.ValueStart, row.ValueLength);
        return ParseDeclarationAttribute(text, name);
    }

    private static string? ParseDeclarationAttribute(string declarationText, string attributeName)
    {
        var span = declarationText.AsSpan();
        while (!span.IsEmpty)
        {
            span = span.TrimStart();
            if (span.IsEmpty) break;

            var equalsIndex = span.IndexOf('=');
            if (equalsIndex < 0) break;

            var name = span[..equalsIndex].Trim();
            span = span[(equalsIndex + 1)..].TrimStart();
            if (span.IsEmpty) break;

            var quote = span[0];
            if (quote is not ('\'' or '"')) break;

            span = span[1..];
            var closeQuote = span.IndexOf(quote);
            if (closeQuote < 0) break;

            var value = span[..closeQuote];
            span = span[(closeQuote + 1)..];

            if (name.Equals(attributeName.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private void WriteElementTo(Utf8XmlWriter writer, int elementIdx)
    {
        ref readonly DbRow row = ref _db.GetRow(elementIdx);
        string localName = GetName(row.NameStart, row.NameLength);
        string prefix = row.PrefixLength > 0 ? GetName(row.NameStart - row.PrefixLength - 1, row.PrefixLength) : string.Empty;
        string nsUri = row.NsUriLength > 0 ? GetName(row.NsUriStart, row.NsUriLength) : string.Empty;

        writer.WriteStartElement(localName, nsUri, prefix);

        // Write attributes
        int attrStart = elementIdx + 1;
        for (int i = 0; i < row.AttributeCount; i++)
        {
            int attrIdx = attrStart + i;
            ref readonly DbRow attrRow = ref _db.GetRow(attrIdx);
            string attrLocalName = GetName(attrRow.NameStart, attrRow.NameLength);
            string attrPrefix = attrRow.PrefixLength > 0 ? GetName(attrRow.NameStart - attrRow.PrefixLength - 1, attrRow.PrefixLength) : string.Empty;
            string attrNsUri = attrRow.NsUriLength > 0 ? GetName(attrRow.NsUriStart, attrRow.NsUriLength) : string.Empty;
            string attrValue = GetDecodedValue(attrRow.ValueStart, attrRow.ValueLength);
            writer.WriteAttributeString(attrLocalName, attrValue, attrNsUri, attrPrefix);
        }

        // Write children
        int childStart = attrStart + row.AttributeCount;
        int endIdx = row.EndIndex;
        int i2 = childStart;
        while (i2 < endIdx)
        {
            ref readonly DbRow child = ref _db.GetRow(i2);
            switch (child.NodeType)
            {
                case XmlNodeType.Element:
                    WriteElementTo(writer, i2);
                    i2 = child.EndIndex;
                    break;
                case XmlNodeType.Text:
                    writer.WriteString(GetDecodedValue(child.ValueStart, child.ValueLength));
                    i2++;
                    break;
                case XmlNodeType.CData:
                    writer.WriteCData(GetDecodedValue(child.ValueStart, child.ValueLength));
                    i2++;
                    break;
                case XmlNodeType.Comment:
                    writer.WriteComment(GetString(child.ValueStart, child.ValueLength));
                    i2++;
                    break;
                case XmlNodeType.ProcessingInstruction:
                    writer.WriteProcessingInstruction(
                        GetName(child.NameStart, child.NameLength),
                        GetString(child.ValueStart, child.ValueLength));
                    i2++;
                    break;
                default:
                    i2++;
                    break;
            }
        }

        writer.WriteEndElement();
    }

    private void WriteToTextWriter(TextWriter textWriter)
    {
        if (HasDeclaration)
        {
            ref readonly DbRow declRow = ref _db.GetRow(_declarationIndex);
            string declText = GetString(declRow.ValueStart, declRow.ValueLength);
            textWriter.Write("<?xml ");
            textWriter.Write(declText);
            textWriter.Write("?>");
        }

        WriteElementToText(textWriter, _rootIndex);
    }

    private void WriteElementToText(TextWriter writer, int elementIdx)
    {
        ref readonly DbRow row = ref _db.GetRow(elementIdx);
        string localName = GetName(row.NameStart, row.NameLength);
        string prefix = row.PrefixLength > 0 ? GetName(row.NameStart - row.PrefixLength - 1, row.PrefixLength) : string.Empty;

        writer.Write('<');
        WriteQualifiedName(writer, prefix, localName);

        // Write attributes
        int attrStart = elementIdx + 1;
        for (int i = 0; i < row.AttributeCount; i++)
        {
            int attrIdx = attrStart + i;
            ref readonly DbRow attrRow = ref _db.GetRow(attrIdx);
            string attrLocalName = GetName(attrRow.NameStart, attrRow.NameLength);
            string attrPrefix = attrRow.PrefixLength > 0 ? GetName(attrRow.NameStart - attrRow.PrefixLength - 1, attrRow.PrefixLength) : string.Empty;

            writer.Write(' ');
            WriteQualifiedName(writer, attrPrefix, attrLocalName);
            writer.Write("=\"");
            WriteRawValue(writer, attrRow.ValueStart, attrRow.ValueLength);
            writer.Write('"');
        }

        // Write children
        int childStart = attrStart + row.AttributeCount;
        int endIdx = row.EndIndex;
        if (childStart == endIdx)
        {
            writer.Write(" />");
            return;
        }

        writer.Write('>');

        int i2 = childStart;
        while (i2 < endIdx)
        {
            ref readonly DbRow child = ref _db.GetRow(i2);
            switch (child.NodeType)
            {
                case XmlNodeType.Element:
                    WriteElementToText(writer, i2);
                    i2 = child.EndIndex;
                    break;
                case XmlNodeType.Text:
                    WriteTextValue(writer, child.ValueStart, child.ValueLength);
                    i2++;
                    break;
                case XmlNodeType.CData:
                    writer.Write("<![CDATA[");
                    WriteRawValue(writer, child.ValueStart, child.ValueLength);
                    writer.Write("]]>");
                    i2++;
                    break;
                case XmlNodeType.Comment:
                    writer.Write("<!--");
                    writer.Write(GetString(child.ValueStart, child.ValueLength));
                    writer.Write("-->");
                    i2++;
                    break;
                case XmlNodeType.ProcessingInstruction:
                    writer.Write("<?");
                    writer.Write(GetName(child.NameStart, child.NameLength));
                    if (child.ValueLength > 0)
                    {
                        writer.Write(' ');
                        writer.Write(GetString(child.ValueStart, child.ValueLength));
                    }
                    writer.Write("?>");
                    i2++;
                    break;
                default:
                    i2++;
                    break;
            }
        }

        writer.Write("</");
        WriteQualifiedName(writer, prefix, localName);
        writer.Write('>');
    }

    private static void WriteQualifiedName(TextWriter writer, string prefix, string localName)
    {
        if (!string.IsNullOrEmpty(prefix))
        {
            writer.Write(prefix);
            writer.Write(':');
        }
        writer.Write(localName);
    }

    /// <summary>
    /// Writes raw UTF-8 source bytes directly to the TextWriter.
    /// The source already contains properly escaped XML (entities preserved as-is).
    /// </summary>
    private void WriteRawValue(TextWriter writer, int start, int length)
    {
        if (length == 0) return;
#if NET
        Span<char> stackBuf = stackalloc char[256];
        var span = _utf8Source.AsSpan(start, length);
        // For short values, use stack buffer
        if (length <= 256)
        {
            int charCount = s_utf8.GetChars(span, stackBuf);
            writer.Write(stackBuf[..charCount]);
        }
        else
        {
            writer.Write(s_utf8.GetString(span));
        }
#else
        writer.Write(s_utf8.GetString(_utf8Source, start, length));
#endif
    }

    /// <summary>
    /// Writes text node value directly from source (already XML-escaped in the source).
    /// </summary>
    private void WriteTextValue(TextWriter writer, int start, int length)
    {
        // Text node values in the parsed source are already entity-escaped
        // (e.g., &amp; &lt; &gt;) so we can write them verbatim.
        WriteRawValue(writer, start, length);
    }

    private static string EscapeText(string value) => value
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    private static string EscapeAttributeValue(string value) => EscapeText(value)
        .Replace("\"", "&quot;")
        .Replace("'", "&apos;")
        .Replace("\r", "&#xD;")
        .Replace("\n", "&#xA;")
        .Replace("\t", "&#x9;");

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            return segment.Array is null
                ? memoryStream.ToArray()
                : segment.AsSpan(0, checked((int)memoryStream.Length)).ToArray();
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memoryStream && memoryStream.TryGetBuffer(out var segment))
        {
            return segment.Array is null
                ? memoryStream.ToArray()
                : segment.AsSpan(0, checked((int)memoryStream.Length)).ToArray();
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
