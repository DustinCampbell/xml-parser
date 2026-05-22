using System.Buffers;
using System.IO;
using System.Text;
using Xunit;

namespace System.Text.Xml.Tests;

public class Utf8XmlWriterTests
{
    [Fact]
    public void Write_simple_elements_produces_expected_xml()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteString("text");
        writer.WriteEndElement();

        Assert.Equal("<root>text</root>", GetString(buffer));
    }

    [Fact]
    public void Write_nested_elements_produces_expected_structure()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteStartElement("parent");
        writer.WriteStartElement("child");
        writer.WriteString("value");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();

        Assert.Equal("<root><parent><child>value</child></parent></root>", GetString(buffer));
    }

    [Fact]
    public void Write_attributes_serializes_attribute_values()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteAttributeString("id", "42");
        writer.WriteAttributeString("name", "sample");
        writer.WriteEndElement();

        Assert.Equal("<root id=\"42\" name=\"sample\" />", GetString(buffer));
    }

    [Fact]
    public void Write_text_escapes_special_characters()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteString("<&>\"'");
        writer.WriteEndElement();

        Assert.Equal("<root>&lt;&amp;&gt;\"'</root>", GetString(buffer));
    }

    [Fact]
    public void Write_cdata_preserves_literal_content()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteCData("<escaped />");
        writer.WriteEndElement();

        Assert.Equal("<root><![CDATA[<escaped />]]></root>", GetString(buffer));
    }

    [Fact]
    public void Write_comment_serializes_comment_markup()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteComment("note");
        writer.WriteEndElement();

        Assert.Equal("<root><!--note--></root>", GetString(buffer));
    }

    [Fact]
    public void Write_processing_instruction_serializes_processing_instruction()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true, ConformanceLevel = XmlConformanceLevel.Fragment });

        writer.WriteProcessingInstruction("xml-stylesheet", "href=\"site.xsl\" type=\"text/xsl\"");
        writer.WriteStartElement("root");
        writer.WriteEndElement();

        Assert.Equal("<?xml-stylesheet href=\"site.xsl\" type=\"text/xsl\"?><root />", GetString(buffer));
    }

    [Fact]
    public void Write_indented_output_honors_options()
    {
        var options = new XmlWriterOptions { OmitXmlDeclaration = true, Indented = true, IndentSize = 2, IndentCharacter = ' ', NewLine = "\n" };
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, options);

        writer.WriteStartElement("root");
        writer.WriteStartElement("child");
        writer.WriteString("value");
        writer.WriteEndElement();
        writer.WriteEndElement();

        var xml = GetString(buffer);
        Assert.Contains("\n", xml);
        Assert.Contains("  <child>", xml);
    }

    [Fact]
    public void Write_compact_output_omits_indentation()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true, Indented = false });

        writer.WriteStartElement("root");
        writer.WriteStartElement("child");
        writer.WriteEndElement();
        writer.WriteEndElement();

        Assert.Equal("<root><child /></root>", GetString(buffer));
    }

    [Fact]
    public void Write_namespaces_emits_namespace_declarations()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteStartElement("child", "http://example.com", "ns");
        writer.WriteAttributeString("attr", "value");
        writer.WriteString("text");
        writer.WriteEndElement();
        writer.WriteEndElement();

        var xml = GetString(buffer);
        Assert.Contains("<ns:child", xml);
        Assert.Contains("xmlns:ns=\"http://example.com\"", xml);
    }

    [Fact]
    public void Write_empty_elements_as_self_closing_tags()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("br");
        writer.WriteEndElement();

        Assert.Equal("<br />", GetString(buffer));
    }

    [Fact]
    public void Write_xml_declaration_when_requested()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer);

        writer.WriteStartDocument();
        writer.WriteStartElement("root");
        writer.WriteEndElement();

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", GetString(buffer));
    }

    [Fact]
    public void Bytes_pending_and_committed_track_stream_output()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8XmlWriter(stream, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteString("text");
        writer.WriteEndElement();

        Assert.True(writer.BytesPending > 0);
        Assert.Equal(0, writer.BytesCommitted);

        writer.Flush();

        Assert.Equal(0, writer.BytesPending);
        Assert.True(writer.BytesCommitted > 0);
    }

    [Fact]
    public void Flush_writes_buffered_content_to_stream()
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8XmlWriter(stream, new XmlWriterOptions { OmitXmlDeclaration = true });

        writer.WriteStartElement("root");
        writer.WriteString("text");
        writer.WriteEndElement();
        writer.Flush();

        Assert.Equal("<root>text</root>", Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Writer_supports_ibufferwriter_and_stream_targets()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("root");
            writer.WriteEndElement();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8XmlWriter(stream, new XmlWriterOptions { OmitXmlDeclaration = true }))
        {
            writer.WriteStartElement("root");
            writer.WriteEndElement();
            writer.Flush();
        }

        Assert.Equal("<root />", GetString(buffer));
        Assert.Equal("<root />", Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string GetString(ArrayBufferWriter<byte> buffer) => Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray());
}
