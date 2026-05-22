using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace System.Text.Xml.Tests;

public class EquivalenceTests
{
    public static IEnumerable<object[]> XmlSamples()
    {
        yield return new object[] { "<root>text</root>" };
        yield return new object[] { "<root><parent><child attr=\"value\">text</child></parent></root>" };
        yield return new object[] { "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns:ns=\"http://example.com\"><ns:child attr=\"value\">text</ns:child></root>" };
        yield return new object[] { "<root><![CDATA[<escaped />]]><!--comment--><child attr=\"42\">value</child></root>" };
        yield return new object[] { "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:a=\"urn:alpha\" xmlns:b=\"urn:beta\"><soap:Header><a:token>abc</a:token></soap:Header><soap:Body><b:request id=\"7\"><b:item>value</b:item></b:request></soap:Body></soap:Envelope>" };
        yield return new object[] { "<fragment><div class=\"card\"><span>Hello</span><br /><em>world</em></div></fragment>" };
    }

    [Theory]
    [MemberData(nameof(XmlSamples))]
    public void Parse_matches_System_XmlDocument_structure(string xml)
    {
        using var document = XmlDocument.Parse(xml);
        var actual = XmlSnapshot.From(document.Root);

        var systemDocument = new System.Xml.XmlDocument();
        systemDocument.LoadXml(xml);
        var expected = XmlSnapshot.From(systemDocument.DocumentElement!);

        Assert.Equal(expected.RootLocalName, actual.RootLocalName);
        Assert.Equal(expected.RootNamespaceUri, actual.RootNamespaceUri);
        Assert.Equal(expected.ElementLocalNames, actual.ElementLocalNames);
        Assert.Equal(expected.ElementNamespaceUris, actual.ElementNamespaceUris);
        Assert.Equal(expected.AttributeValues, actual.AttributeValues);
        Assert.Equal(expected.CommentCount, actual.CommentCount);
        Assert.Equal(expected.CDataCount, actual.CDataCount);
        Assert.Equal(expected.InnerText, actual.InnerText);
    }

    [Theory]
    [MemberData(nameof(XmlSamples))]
    public void Parse_matches_XDocument_structure(string xml)
    {
        using var document = XmlDocument.Parse(xml);
        var actual = XmlSnapshot.From(document.Root);
        var expected = XmlSnapshot.From(XDocument.Parse(xml, LoadOptions.PreserveWhitespace).Root!);

        Assert.Equal(expected.RootLocalName, actual.RootLocalName);
        Assert.Equal(expected.RootNamespaceUri, actual.RootNamespaceUri);
        Assert.Equal(expected.ElementLocalNames, actual.ElementLocalNames);
        Assert.Equal(expected.ElementNamespaceUris, actual.ElementNamespaceUris);
        Assert.Equal(expected.AttributeValues, actual.AttributeValues);
        Assert.Equal(expected.InnerText, actual.InnerText);
    }

    [Fact]
    public void Write_matches_linq_to_xml_for_namespaced_sample()
    {
        XNamespace ns = "http://example.com";
        var expected = new XDocument(
            new XElement("root",
                new XElement(ns + "child", new XAttribute("attr", "value"), "text")));

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8XmlWriter(buffer, new XmlWriterOptions { OmitXmlDeclaration = true });
        writer.WriteStartElement("root");
        writer.WriteStartElement("child", ns.NamespaceName, "ns");
        writer.WriteAttributeString("attr", "value");
        writer.WriteString("text");
        writer.WriteEndElement();
        writer.WriteEndElement();

        var actual = XDocument.Parse(Encoding.UTF8.GetString(buffer.WrittenSpan.ToArray()), LoadOptions.PreserveWhitespace).Root!;
        var expectedRoot = expected.Root!;

        Assert.Equal(expectedRoot.Name.LocalName, actual.Name.LocalName);
        Assert.Equal(expectedRoot.Elements().Single().Name.LocalName, actual.Elements().Single().Name.LocalName);
        Assert.Equal(expectedRoot.Elements().Single().Name.NamespaceName, actual.Elements().Single().Name.NamespaceName);
        Assert.Equal(expectedRoot.Elements().Single().Attribute("attr")?.Value, actual.Elements().Single().Attribute("attr")?.Value);
        Assert.Equal(expectedRoot.Elements().Single().Value, actual.Elements().Single().Value);
    }

    [Fact]
    public void Namespace_resolution_matches_System_Xml_behavior()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns:ns=\"http://example.com\"><ns:child attr=\"value\">text</ns:child></root>";

        using var document = XmlDocument.Parse(xml);
        var child = Assert.Single(document.Root.Elements());

        var systemDocument = new System.Xml.XmlDocument();
        systemDocument.LoadXml(xml);
        var systemChild = (System.Xml.XmlElement)systemDocument.DocumentElement!.FirstChild!;

        Assert.Equal(systemChild.Prefix, child.Prefix);
        Assert.Equal(systemChild.LocalName, child.LocalName);
        Assert.Equal(systemChild.NamespaceURI, child.NamespaceUri);
        Assert.Equal(systemChild.GetAttribute("attr"), child.GetAttribute("attr")?.Value);
    }
}

internal sealed record XmlSnapshot(
    string RootLocalName,
    string RootNamespaceUri,
    IReadOnlyList<string> ElementLocalNames,
    IReadOnlyList<string> ElementNamespaceUris,
    IReadOnlyDictionary<string, string> AttributeValues,
    int CommentCount,
    int CDataCount,
    string InnerText)
{
    public static XmlSnapshot From(XmlElement root)
    {
        var elements = new List<XmlElement> { root };
        elements.AddRange(root.Descendants());
        var attributes = new Dictionary<string, string>();
        foreach (var element in elements)
        {
            var attrEnum = element.EnumerateAttributes();
            while (attrEnum.MoveNext())
            {
                var attr = attrEnum.Current;
                attributes[$"{element.LocalName}@{attr.LocalName}"] = attr.Value;
            }
        }

        // Count comments and CDATA in child nodes
        int commentCount = 0;
        int cdataCount = 0;
        foreach (var element in elements)
        {
            var childEnum = element.EnumerateChildren();
            while (childEnum.MoveNext())
            {
                var child = childEnum.Current;
                if (child.NodeType == XmlNodeType.Comment) commentCount++;
                else if (child.NodeType == XmlNodeType.CData) cdataCount++;
            }
        }

        return new XmlSnapshot(
            root.LocalName,
            root.NamespaceUri,
            elements.Select(element => element.LocalName).ToList(),
            elements.Select(element => element.NamespaceUri).ToList(),
            attributes,
            commentCount,
            cdataCount,
            root.InnerText);
    }

    public static XmlSnapshot From(System.Xml.XmlElement root)
    {
        var elements = root.SelectNodes("descendant-or-self::*")!.Cast<System.Xml.XmlElement>().ToList();
        var attributes = elements
            .SelectMany(element => element.Attributes.Cast<System.Xml.XmlAttribute>()
                .Select(attribute => new KeyValuePair<string, string>($"{element.LocalName}@{attribute.LocalName}", attribute.Value)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return new XmlSnapshot(
            root.LocalName,
            root.NamespaceURI,
            elements.Select(element => element.LocalName).ToList(),
            elements.Select(element => element.NamespaceURI).ToList(),
            attributes,
            root.SelectNodes("descendant-or-self::comment()")!.Count,
            root.SelectNodes("descendant-or-self::node()")!.Cast<global::System.Xml.XmlNode>().Count(node => node is XmlCDataSection),
            root.InnerText);
    }

    public static XmlSnapshot From(XElement root)
    {
        var elements = root.DescendantsAndSelf().ToList();
        var attributes = elements
            .SelectMany(element => element.Attributes()
                .Select(attribute => new KeyValuePair<string, string>($"{element.Name.LocalName}@{attribute.Name.LocalName}", attribute.Value)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        return new XmlSnapshot(
            root.Name.LocalName,
            root.Name.NamespaceName,
            elements.Select(element => element.Name.LocalName).ToList(),
            elements.Select(element => element.Name.NamespaceName).ToList(),
            attributes,
            root.DescendantNodesAndSelf().OfType<XComment>().Count(),
            root.DescendantNodes().OfType<XCData>().Count(),
            root.Value);
    }
}

internal static class XmlAssert
{
    public static string Normalize(string xml) => XDocument.Parse(xml, LoadOptions.PreserveWhitespace).ToString(SaveOptions.DisableFormatting);
}
