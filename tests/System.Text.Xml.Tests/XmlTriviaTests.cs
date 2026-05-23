using Xunit;

namespace System.Text.Xml.Tests;

public class XmlTriviaTests
{
    [Fact]
    public void Parse_WithPreserveTrivia_CapturesWhitespaceBetweenElements()
    {
        var xml = "<root>\n  <child/>\n  <child/>\n</root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        Assert.True(doc.HasTrivia);

        // The second <child/> should have leading whitespace trivia ("\n  ")
        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var first = children.Current;
        Assert.True(children.MoveNext());
        var second = children.Current;

        var leading = second.GetLeadingTrivia();
        Assert.False(leading.IsEmpty);
        Assert.Equal(1, leading.Count);
        Assert.Equal(XmlTriviaKind.Whitespace, leading[0].Kind);
        Assert.Equal("\n  ", leading[0].Text);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_CapturesCommentsAsTrivia()
    {
        var xml = "<root><!-- comment --><child/></root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var child = children.Current;

        var leading = child.GetLeadingTrivia();
        Assert.False(leading.IsEmpty);
        Assert.Equal(1, leading.Count);
        Assert.Equal(XmlTriviaKind.Comment, leading[0].Kind);
        Assert.Equal(" comment ", leading[0].Text);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_CapturesTrailingWhitespace()
    {
        var xml = "<root><child/>\n</root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        // Trailing whitespace after </child> before </root> should be trailing on root element
        var root = doc.Root;
        var trailing = root.GetTrailingTrivia();
        Assert.False(trailing.IsEmpty);
        Assert.Equal(XmlTriviaKind.Whitespace, trailing[0].Kind);
        Assert.Equal("\n", trailing[0].Text);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_MultipleTriviaBeforeElement()
    {
        var xml = "<root>\n  <!-- hi -->\n  <child/></root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var child = children.Current;

        var leading = child.GetLeadingTrivia();
        Assert.Equal(3, leading.Count);
        Assert.Equal(XmlTriviaKind.Whitespace, leading[0].Kind);
        Assert.Equal(XmlTriviaKind.Comment, leading[1].Kind);
        Assert.Equal(XmlTriviaKind.Whitespace, leading[2].Kind);
    }

    [Fact]
    public void Parse_WithoutPreserveTrivia_NoTriviaAvailable()
    {
        var xml = "<root>\n  <child/>\n</root>";
        using var doc = XmlDocument.Parse(xml);

        Assert.False(doc.HasTrivia);

        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var child = children.Current;

        var leading = child.GetLeadingTrivia();
        Assert.True(leading.IsEmpty);
        Assert.Equal(0, leading.Count);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_StructuralContentUnchanged()
    {
        var xml = "<root>\n  <child attr=\"value\">text</child>\n</root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        var child = doc.Root.EnumerateElements();
        Assert.True(child.MoveNext());
        Assert.Equal("child", child.Current.LocalName);
        Assert.Equal("text", child.Current.InnerText);

        var attr = child.Current.GetAttribute("attr");
        Assert.NotNull(attr);
        Assert.Equal("value", attr.Value.Value);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_TriviaListEnumerator()
    {
        var xml = "<root>\n  <!-- a -->\n  <child/></root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var child = children.Current;

        var leading = child.GetLeadingTrivia();
        int count = 0;
        foreach (var trivia in leading)
        {
            count++;
            Assert.True(trivia.Length > 0);
        }
        Assert.Equal(leading.Count, count);
    }

    [Fact]
    public void XmlTriviaList_ToList_ReturnsCorrectItems()
    {
        var xml = "<root>  <child/></root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        var children = doc.Root.EnumerateElements();
        Assert.True(children.MoveNext());
        var child = children.Current;

        var list = child.GetLeadingTrivia().ToList();
        Assert.Single(list);
        Assert.Equal(XmlTriviaKind.Whitespace, list[0].Kind);
        Assert.Equal("  ", list[0].Text);
    }

    [Fact]
    public void Parse_WithPreserveTrivia_CommentsNotStoredAsStructuralNodes()
    {
        var xml = "<root><!-- comment --><child/></root>";
        var options = new XmlDocumentOptions { PreserveTrivia = true };
        using var doc = XmlDocument.Parse(xml, options);

        // With trivia mode, comments go into trivia, not as child nodes.
        // So root should have 1 element child, not 1 comment + 1 element
        Assert.Equal(1, doc.Root.ChildCount);
    }
}
