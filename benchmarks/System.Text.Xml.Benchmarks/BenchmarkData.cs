using System.Globalization;
using System.Text;

namespace System.Text.Xml.Benchmarks;

public static class BenchmarkData
{
    public static string SmallXml { get; } = CreateSmallXml();

    public static string MediumXml { get; } = CreateMediumXml();

    public static string LargeXml { get; } = CreateLargeXml();

    public static byte[] SmallXmlUtf8 { get; } = Encoding.UTF8.GetBytes(SmallXml);

    public static byte[] MediumXmlUtf8 { get; } = Encoding.UTF8.GetBytes(MediumXml);

    public static byte[] LargeXmlUtf8 { get; } = Encoding.UTF8.GetBytes(LargeXml);

    public static string GetXml(string size) => size switch
    {
        "Small" => SmallXml,
        "Medium" => MediumXml,
        "Large" => LargeXml,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown benchmark dataset."),
    };

    public static byte[] GetUtf8(string size) => size switch
    {
        "Small" => SmallXmlUtf8,
        "Medium" => MediumXmlUtf8,
        "Large" => LargeXmlUtf8,
        _ => throw new ArgumentOutOfRangeException(nameof(size), size, "Unknown benchmark dataset."),
    };

    private static string CreateSmallXml() =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><catalog><item id=\"1\" category=\"books\">First item</item><item id=\"2\" category=\"music\">Second item</item><item id=\"3\" category=\"games\">Third item</item><item id=\"4\" category=\"tools\">Fourth item</item><item id=\"5\" category=\"movies\">Fifth item</item></catalog>";

    private static string CreateMediumXml()
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><catalog>");

        for (var i = 0; i < 50; i++)
        {
            builder.Append("<item id=\"");
            builder.Append(i);
            builder.Append("\" category=\"group");
            builder.Append(i % 5);
            builder.Append("\" enabled=\"");
            builder.Append(i % 2 == 0 ? "true" : "false");
            builder.Append("\"><name>Item ");
            builder.Append(i);
            builder.Append("</name><value>");
            builder.Append(i * 13);
            builder.Append("</value><description>Description for item ");
            builder.Append(i);
            builder.Append("</description></item>");
        }

        builder.Append("</catalog>");
        return builder.ToString();
    }

    private static string CreateLargeXml()
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><catalog xmlns=\"urn:catalog\" xmlns:meta=\"urn:meta\" meta:version=\"2.0\">");

        for (var section = 0; section < 12; section++)
        {
            builder.Append("<section id=\"section-");
            builder.Append(section);
            builder.Append("\" category=\"category-");
            builder.Append(section % 4);
            builder.Append("\"><meta:info created=\"2024-01-");
            builder.Append((section % 28 + 1).ToString("00", CultureInfo.InvariantCulture));
            builder.Append("\">Section ");
            builder.Append(section);
            builder.Append("</meta:info><items>");

            for (var item = 0; item < 50; item++)
            {
                var index = section * 50 + item;
                builder.Append("<item id=\"item-");
                builder.Append(index);
                builder.Append("\" status=\"");
                builder.Append(index % 3 == 0 ? "active" : index % 3 == 1 ? "archived" : "draft");
                builder.Append("\" meta:rank=\"");
                builder.Append(index % 10);
                builder.Append("\"><name>Item ");
                builder.Append(index);
                builder.Append("</name><description>Detailed description for item ");
                builder.Append(index);
                builder.Append("</description><tags><tag>alpha</tag><tag>beta</tag><tag>group-");
                builder.Append(section);
                builder.Append("</tag></tags><metrics><metric name=\"length\">");
                builder.Append(index % 100);
                builder.Append("</metric><metric name=\"weight\">");
                builder.Append((index * 1.25).ToString("0.##", CultureInfo.InvariantCulture));
                builder.Append("</metric></metrics></item>");
            }

            builder.Append("</items></section>");
        }

        builder.Append("</catalog>");
        return builder.ToString();
    }
}
