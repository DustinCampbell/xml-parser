using BenchmarkDotNet.Attributes;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Xml;
using System.Xml;
using System.Xml.Linq;
using SystemTextXmlWriter = System.Text.Xml.Utf8XmlWriter;
using SystemTextXmlWriterOptions = System.Text.Xml.XmlWriterOptions;

namespace System.Text.Xml.Benchmarks;

[MemoryDiagnoser]
public class WritingBenchmarks
{
    [Params(10, 100, 1000)]
    public int ElementCount { get; set; }

    [Benchmark]
    public int WriteWithSystemTextXml()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new SystemTextXmlWriter(buffer, new SystemTextXmlWriterOptions { Indented = false });

        writer.WriteStartDocument();
        WriteWithSystemTextXml(writer, ElementCount);
        writer.WriteEndDocument();
        writer.Flush();

        return buffer.WrittenCount;
    }

    [Benchmark]
    public int WriteWithSystemXml()
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Indent = false, Encoding = System.Text.Encoding.UTF8 }))
        {
            writer.WriteStartDocument();
            WriteWithSystemXml(writer, ElementCount);
            writer.WriteEndDocument();
        }

        return checked((int)stream.Length);
    }

    [Benchmark]
    public int WriteWithXLinq()
    {
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "catalog",
                new XAttribute("version", "1.0"),
                Enumerable.Range(0, ElementCount).Select(CreateItemElement)));

        using var stream = new MemoryStream();
        document.Save(stream, SaveOptions.DisableFormatting);
        return checked((int)stream.Length);
    }

    private static void WriteWithSystemTextXml(SystemTextXmlWriter writer, int elementCount)
    {
        writer.WriteStartElement("catalog", string.Empty, string.Empty);
        writer.WriteAttributeString("version", "1.0", string.Empty, string.Empty);

        for (var i = 0; i < elementCount; i++)
        {
            writer.WriteStartElement("item", string.Empty, string.Empty);
            writer.WriteAttributeString("id", i.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty);
            writer.WriteAttributeString("category", $"category-{i % 5}", string.Empty, string.Empty);

            writer.WriteStartElement("name", string.Empty, string.Empty);
            writer.WriteString($"Item {i}");
            writer.WriteEndElement();

            writer.WriteStartElement("description", string.Empty, string.Empty);
            writer.WriteString($"Description for item {i}");
            writer.WriteEndElement();

            writer.WriteStartElement("status", string.Empty, string.Empty);
            writer.WriteString(i % 2 == 0 ? "active" : "inactive");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteWithSystemXml(XmlWriter writer, int elementCount)
    {
        writer.WriteStartElement("catalog");
        writer.WriteAttributeString("version", "1.0");

        for (var i = 0; i < elementCount; i++)
        {
            writer.WriteStartElement("item");
            writer.WriteAttributeString("id", i.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("category", $"category-{i % 5}");

            writer.WriteStartElement("name");
            writer.WriteString($"Item {i}");
            writer.WriteEndElement();

            writer.WriteStartElement("description");
            writer.WriteString($"Description for item {i}");
            writer.WriteEndElement();

            writer.WriteStartElement("status");
            writer.WriteString(i % 2 == 0 ? "active" : "inactive");
            writer.WriteEndElement();

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static XElement CreateItemElement(int index) =>
        new(
            "item",
            new XAttribute("id", index),
            new XAttribute("category", $"category-{index % 5}"),
            new XElement("name", $"Item {index}"),
            new XElement("description", $"Description for item {index}"),
            new XElement("status", index % 2 == 0 ? "active" : "inactive"));
}
