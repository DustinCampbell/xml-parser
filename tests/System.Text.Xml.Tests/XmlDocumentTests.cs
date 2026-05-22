using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace System.Text.Xml.Tests;

public class XmlDocumentTests
{
    [Fact]
    public void Parse_simple_xml_sets_root_and_inner_text()
    {
        using var document = XmlDocument.Parse("<root>text</root>");

        Assert.Equal("root", document.Root.LocalName);
        Assert.Equal("text", document.Root.InnerText);
    }

    [Fact]
    public void Parse_complex_nested_xml_preserves_hierarchy()
    {
        using var document = XmlDocument.Parse("<catalog><book id=\"b1\"><title>Example</title><author>Copilot</author></book><book id=\"b2\"><title>Second</title></book></catalog>");

        Assert.Equal(2, document.Root.Elements("book").Count());
        Assert.Equal(5, document.Root.Descendants().Count());
        Assert.Contains(document.Root.Descendants(), element => element.LocalName == "author");
    }

    [Fact]
    public void Parse_with_namespaces_preserves_prefix_and_namespace_uri()
    {
        using var document = XmlDocument.Parse("<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns:ns=\"http://example.com\"><ns:child attr=\"value\">text</ns:child></root>");
        var child = Assert.Single(document.Root.Elements());

        Assert.Equal("ns", child.Prefix);
        Assert.Equal("child", child.LocalName);
        Assert.Equal("http://example.com", child.NamespaceUri);
    }

    [Fact]
    public void Parse_with_attributes_exposes_attribute_nodes()
    {
        using var document = XmlDocument.Parse("<root id=\"42\" name=\"sample\" />");

        Assert.Equal("42", document.Root.GetAttribute("id")?.Value);
        Assert.Equal("sample", document.Root.GetAttribute("name")?.Value);
    }

    [Fact]
    public void Load_from_string_and_utf8_bytes_yields_equivalent_documents()
    {
        const string xml = "<root><child>value</child></root>";
        using var fromString = XmlDocument.Parse(xml);
        using var fromBytes = XmlDocument.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(fromString.ToString(), fromBytes.ToString());
    }

    [Fact]
    public void Save_to_stream_and_to_string_return_serialized_xml()
    {
        using var document = XmlDocument.Parse("<root><child>value</child></root>");
        using var stream = new MemoryStream();

        document.Save(stream);

        Assert.Equal(document.ToString(), Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void Parse_then_save_round_trips_equivalent_xml()
    {
        const string xml = "<root><child attr=\"value\">text</child><![CDATA[cdata]]><!--comment--></root>";
        using var document = XmlDocument.Parse(xml);

        Assert.Equal(XmlAssert.Normalize(xml), XmlAssert.Normalize(document.ToString()));
    }

    [Fact]
    public void Navigation_apis_expose_root_children_elements_and_descendants()
    {
        using var document = XmlDocument.Parse("<root><child id=\"1\" /><child id=\"2\"><grandchild /></child></root>");
        var children = document.Root.Elements().ToList();
        var descendants = document.Root.Descendants().ToList();

        Assert.Equal(2, children.Count);
        Assert.Equal(3, descendants.Count);
        Assert.Equal("1", children[0].GetAttribute("id")?.Value);
        Assert.Contains(descendants, node => node.LocalName == "grandchild");
    }

    [Fact]
    public void Modifying_document_supports_addchild_removechild_and_setattribute()
    {
        var root = new XmlElementNode(new XmlName("root"));
        var child = new XmlElementNode(new XmlName("added"));
        child.AddChild(new XmlTextNode("payload"));
        child.SetAttribute(new XmlAttributeNode(new XmlName("id"), "7"));

        root.AddChild(child);
        Assert.Equal("7", Assert.Single(root.Elements()).GetAttribute("id")?.Value);

        Assert.True(root.RemoveChild(child));
        Assert.Empty(root.Elements());
    }

    [Fact]
    public void InnerText_concatenates_text_from_nested_nodes()
    {
        using var document = XmlDocument.Parse("<root>Hello <child>beautiful</child> world<![CDATA[!]]></root>");

        Assert.Equal("Hello beautiful world!", document.Root.InnerText);
    }
}
