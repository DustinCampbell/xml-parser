using System.Linq;
using System.Text;
using Xunit;

namespace System.Text.Xml.Tests;

public class Utf8XmlReaderTests
{
    [Fact]
    public void Read_simple_elements_returns_expected_sequence()
    {
        var tokens = ReadAll("<root>text</root>");

        Assert.Collection(
            tokens,
            token => AssertToken(token, XmlTokenType.StartElement, "root", depth: 0),
            token => AssertToken(token, XmlTokenType.Text, value: "text", depth: 1),
            token => AssertToken(token, XmlTokenType.EndElement, "root", depth: 0));
    }

    [Fact]
    public void Read_nested_elements_tracks_depth()
    {
        var tokens = ReadAll("<root><parent><child>value</child></parent></root>");

        Assert.Equal(3, tokens.Max(token => token.Depth));
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.StartElement && token.LocalName == "parent" && token.Depth == 1);
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.StartElement && token.LocalName == "child" && token.Depth == 2);
    }

    [Fact]
    public void Read_attributes_exposes_attribute_tokens()
    {
        var tokens = ReadAll("<root id=\"42\" name=\"sample\" />");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.Attribute && token.LocalName == "id" && token.Value == "42");
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.Attribute && token.LocalName == "name" && token.Value == "sample");
    }

    [Fact]
    public void Read_self_closing_elements_emit_synthetic_end_elements()
    {
        var tokens = ReadAll("<root><br/></root>");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.StartElement && token.LocalName == "br" && token.IsEmptyElement);
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.EndElement && token.LocalName == "br" && token.Depth == 1);
    }

    [Fact]
    public void Read_cdata_sections_returns_cdata_tokens()
    {
        var tokens = ReadAll("<root><![CDATA[<escaped>content</escaped>]]></root>");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.CData && token.Value == "<escaped>content</escaped>");
    }

    [Fact]
    public void Read_comments_returns_comment_tokens_when_enabled()
    {
        var tokens = ReadAll("<root><!--note--><child /></root>", new XmlReaderOptions { CommentHandling = XmlCommentHandling.Allow });

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.Comment && token.Value == "note");
    }

    [Fact]
    public void Read_processing_instructions_returns_processing_instruction_tokens()
    {
        var tokens = ReadAll("<root><?pi value?><child /></root>");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.ProcessingInstruction && token.LocalName == "pi" && token.Value == "value");
    }

    [Fact]
    public void Read_xml_declaration_returns_xml_declaration_token()
    {
        var tokens = ReadAll("<?xml version=\"1.0\" encoding=\"utf-8\"?><root />");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.XmlDeclaration && token.Value == "version=\"1.0\" encoding=\"utf-8\"");
    }

    [Fact]
    public void Read_namespaces_resolves_prefixes_and_namespace_uris()
    {
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:default\" xmlns:ns=\"http://example.com\"><ns:child attr=\"value\">text</ns:child></root>";
        var tokens = ReadAll(xml);

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.StartElement && token.LocalName == "root" && token.NamespaceUri == "urn:default");
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.StartElement && token.Prefix == "ns" && token.LocalName == "child" && token.NamespaceUri == "http://example.com");
        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.Attribute && token.Prefix == "xmlns" && token.LocalName == "ns" && token.NamespaceUri == XmlNamespace.Xmlns.Uri && token.Value == "http://example.com");
    }

    [Fact]
    public void Read_entity_references_decodes_predefined_and_numeric_values()
    {
        var tokens = ReadAll("<root>&amp; &lt; &gt; &quot; &apos; &#x41; &#65;</root>");

        Assert.Contains(tokens, token => token.TokenType == XmlTokenType.Text && token.Value == "& < > \" ' A A");
    }

    [Fact]
    public void Read_whitespace_honors_ignore_whitespace_option()
    {
        const string xml = "<root>  <child />\r\n  <child /> </root>";

        var withWhitespace = ReadAll(xml);
        var withoutWhitespace = ReadAll(xml, new XmlReaderOptions { IgnoreWhitespace = true });

        Assert.Contains(withWhitespace, token => token.TokenType == XmlTokenType.Whitespace);
        Assert.DoesNotContain(withoutWhitespace, token => token.TokenType == XmlTokenType.Whitespace);
    }

    [Fact]
    public void Read_malformed_xml_throws_xml_exception()
    {
        var exception = Assert.Throws<XmlException>(() => ReadAll("<root><child></root>"));

        Assert.True(exception.LineNumber > 0);
        Assert.True(exception.BytePositionInLine >= 0);
    }

    [Fact]
    public void Read_max_depth_enforces_reader_options()
    {
        var exception = Assert.Throws<XmlException>(() => ConsumeAll("<a><b><c /></b></a>", new XmlReaderOptions { MaxDepth = 2 }));

        Assert.Contains("maximum depth", exception.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skip_skips_the_current_subtree()
    {
        var bytes = Encoding.UTF8.GetBytes("<root><skip><inner>value</inner></skip><after /></root>");
        var reader = new Utf8XmlReader(bytes);

        Assert.True(reader.Read());
        Assert.True(reader.Read());
        Assert.Equal(XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("skip", reader.GetLocalName());

        reader.Skip();
        Assert.Equal(XmlTokenType.EndElement, reader.TokenType);
        Assert.Equal("skip", reader.GetLocalName());

        Assert.True(reader.Read());
        Assert.Equal(XmlTokenType.StartElement, reader.TokenType);
        Assert.Equal("after", reader.GetLocalName());
    }

    private static System.Collections.Generic.List<ReaderToken> ReadAll(string xml, XmlReaderOptions options = default)
    {
        var reader = new Utf8XmlReader(Encoding.UTF8.GetBytes(xml), options);
        var tokens = new System.Collections.Generic.List<ReaderToken>();

        while (reader.Read())
        {
            tokens.Add(Capture(in reader));
            if (reader.TokenType == XmlTokenType.StartElement)
            {
                while (reader.MoveToNextAttribute())
                {
                    tokens.Add(Capture(in reader));
                }
            }
        }

        return tokens;
    }

    private static void ConsumeAll(string xml, XmlReaderOptions options)
    {
        var reader = new Utf8XmlReader(Encoding.UTF8.GetBytes(xml), options);
        while (reader.Read())
        {
        }
    }

    private static ReaderToken Capture(in Utf8XmlReader reader) =>
        new(reader.TokenType, reader.GetLocalName(), reader.GetPrefix(), reader.GetNamespaceUri(), reader.GetString(), reader.CurrentDepth, reader.IsEmptyElement);

    private static void AssertToken(ReaderToken token, XmlTokenType tokenType, string? localName = null, string? value = null, int? depth = null)
    {
        Assert.Equal(tokenType, token.TokenType);
        if (localName is not null)
        {
            Assert.Equal(localName, token.LocalName);
        }

        if (value is not null)
        {
            Assert.Equal(value, token.Value);
        }

        if (depth.HasValue)
        {
            Assert.Equal(depth.Value, token.Depth);
        }
    }

    private sealed record ReaderToken(XmlTokenType TokenType, string LocalName, string Prefix, string NamespaceUri, string? Value, int Depth, bool IsEmptyElement);
}
