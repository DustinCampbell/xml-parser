using System.Text;
using Xunit;

namespace System.Text.Xml.Tests;

public class XmlNameTests
{
    [Fact]
    public void Parse_qualified_names_splits_prefix_and_local_name()
    {
        var name = XmlName.Parse("ns:child");

        Assert.Equal("ns", name.Prefix);
        Assert.Equal("child", name.LocalName);
        Assert.Equal(string.Empty, name.NamespaceUri);
    }

    [Fact]
    public void Equality_compares_local_name_prefix_and_namespace_uri()
    {
        var first = new XmlName("child", "ns", "http://example.com");
        var second = new XmlName("child", "ns", "http://example.com");
        var third = new XmlName("child", string.Empty, "http://example.com");

        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Constructor_exposes_prefix_local_name_and_namespace_uri()
    {
        var name = new XmlName("item", "p", "urn:test");

        Assert.Equal("p:item", name.ToString());
        Assert.Equal("item", name.LocalName);
        Assert.Equal("p", name.Prefix);
        Assert.Equal("urn:test", name.NamespaceUri);
    }
}
