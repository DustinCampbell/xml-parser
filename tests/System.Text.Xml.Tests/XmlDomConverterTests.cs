using System.Linq;
using Xunit;

namespace System.Text.Xml.Tests;

public class XmlDomConverterTests
{
    [Fact]
    public void Create_ConvertsSimpleElement()
    {
        using var doc = XmlDocument.Parse("<root><child>text</child></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Equal("root", mutable.LocalName);
        Assert.Single(mutable.Children);
        Assert.IsType<XmlElementNode>(mutable.Children[0]);

        var child = (XmlElementNode)mutable.Children[0];
        Assert.Equal("child", child.LocalName);
        Assert.Equal("text", child.InnerText);
    }

    [Fact]
    public void Create_ConvertsAttributes()
    {
        using var doc = XmlDocument.Parse("<root id=\"123\" class=\"main\"/>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Equal(2, mutable.Attributes.Count);
        Assert.Equal("id", mutable.Attributes[0].LocalName);
        Assert.Equal("123", mutable.Attributes[0].Value);
        Assert.Equal("class", mutable.Attributes[1].LocalName);
        Assert.Equal("main", mutable.Attributes[1].Value);
    }

    [Fact]
    public void Create_ConvertsNamespaces()
    {
        using var doc = XmlDocument.Parse("<ns:root xmlns:ns=\"http://example.com\"><ns:child/></ns:root>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Equal("root", mutable.LocalName);
        Assert.Equal("ns", mutable.Prefix);
        Assert.Equal("http://example.com", mutable.NamespaceUri);
    }

    [Fact]
    public void Create_ConvertsCData()
    {
        using var doc = XmlDocument.Parse("<root><![CDATA[some <data>]]></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Single(mutable.Children);
        var cdata = Assert.IsType<XmlCDataNode>(mutable.Children[0]);
        Assert.Equal("some <data>", cdata.Value);
    }

    [Fact]
    public void Create_ConvertsComments()
    {
        using var doc = XmlDocument.Parse("<root><!-- hello --><child/></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Equal(2, mutable.Children.Count);
        var comment = Assert.IsType<XmlCommentNode>(mutable.Children[0]);
        Assert.Equal(" hello ", comment.Value);
    }

    [Fact]
    public void Create_PreservesTrivia()
    {
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse("<root>\n  <child/>\n</root>", options);

        var mutable = XmlElementNode.Create(doc.Root);
        var children = mutable.Elements().ToList();
        Assert.Single(children);

        var child = children[0];
        Assert.NotEmpty(child.LeadingTrivia);
        Assert.Equal(XmlTriviaKind.Whitespace, child.LeadingTrivia[0].Kind);
        Assert.Equal("\n  ", child.LeadingTrivia[0].Text);
    }

    [Fact]
    public void Create_ThenModify_ThenSerialize()
    {
        using var doc = XmlDocument.Parse("<root><item>one</item></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        // Add a new child
        var newItem = new XmlElementNode(XmlNameAccessor.Create("item"));
        newItem.AddChild(new XmlTextNode("two"));
        mutable.AddChild(newItem);

        var xml = mutable.ToString();
        Assert.Contains("<item>one</item>", xml);
        Assert.Contains("<item>two</item>", xml);
    }

    [Fact]
    public void Create_ThenRemoveChild_ThenSerialize()
    {
        using var doc = XmlDocument.Parse("<root><a/><b/><c/></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        var b = mutable.Elements("b").First();
        mutable.RemoveChild(b);

        var xml = mutable.ToString();
        Assert.Contains("<a", xml);
        Assert.Contains("<c", xml);
        Assert.DoesNotContain("<b", xml);
    }

    [Fact]
    public void ToDocument_RoundTrips()
    {
        using var doc = XmlDocument.Parse("<root><child attr=\"val\">text</child></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        // Modify
        mutable.SetAttribute(new XmlAttributeNode("added", null, null, "true"));

        // Convert back to read-only
        using var readOnly = mutable.ToDocument();
        Assert.Equal("root", readOnly.Root.LocalName);

        var attr = readOnly.Root.GetAttribute("added");
        Assert.NotNull(attr);
        Assert.Equal("true", attr.Value.Value);
    }

    [Fact]
    public void Create_WithTrivia_CanAddTriviaManually()
    {
        using var doc = XmlDocument.Parse("<root><child/></root>");
        var mutable = XmlElementNode.Create(doc.Root);

        // Add whitespace trivia to the child
        var child = mutable.Elements().First();
        child.AddLeadingTrivia(XmlNodeTrivia.Whitespace("\n  "));
        child.AddTrailingTrivia(XmlNodeTrivia.Whitespace("\n"));

        Assert.Single(child.LeadingTrivia);
        Assert.Equal("\n  ", child.LeadingTrivia[0].Text);
        Assert.Single(child.TrailingTrivia);
        Assert.Equal("\n", child.TrailingTrivia[0].Text);
    }

    [Fact]
    public void Create_DeepTree()
    {
        using var doc = XmlDocument.Parse("<a><b><c><d>deep</d></c></b></a>");
        var mutable = XmlElementNode.Create(doc.Root);

        Assert.Equal("a", mutable.LocalName);
        var b = (XmlElementNode)mutable.Children[0];
        Assert.Equal("b", b.LocalName);
        var c = (XmlElementNode)b.Children[0];
        Assert.Equal("c", c.LocalName);
        var d = (XmlElementNode)c.Children[0];
        Assert.Equal("d", d.LocalName);
        Assert.Equal("deep", d.InnerText);
    }
}
