using System.Text;
using Xunit;

namespace System.Text.Xml.Tests;

public class XmlEncodedTextTests
{
    [Fact]
    public void Encode_simple_text_returns_utf8_bytes()
    {
        var encoded = XmlEncodedText.Encode("simple");

        Assert.Equal(Encoding.UTF8.GetBytes("simple"), encoded.EncodedUtf8Bytes.ToArray());
    }

    [Fact]
    public void Encode_text_with_special_characters_escapes_xml_sensitive_content()
    {
        var encoded = XmlEncodedText.Encode("<tag attr=\"value\">π & café</tag>");

        Assert.Equal("&lt;tag attr=&quot;value&quot;&gt;π &amp; café&lt;/tag&gt;", Encoding.UTF8.GetString(encoded.EncodedUtf8Bytes.ToArray()));
        Assert.Equal("<tag attr=\"value\">π & café</tag>", encoded.ToString());
    }

    [Fact]
    public void Encoded_bytes_are_valid_utf8()
    {
        var encoded = XmlEncodedText.Encode("Grüße π");

        Assert.Equal(Encoding.UTF8.GetBytes("Grüße π"), encoded.EncodedUtf8Bytes.ToArray());
    }
}
