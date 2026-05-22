using BenchmarkDotNet.Attributes;
using System.IO;
using System.Text.Xml;
using System.Xml;
using System.Xml.Linq;
using SystemTextXmlDocument = System.Text.Xml.XmlDocument;
using SystemTextXmlReader = System.Text.Xml.Utf8XmlReader;

namespace System.Text.Xml.Benchmarks;

[MemoryDiagnoser]
public class ReadingBenchmarks
{
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreComments = false,
        IgnoreWhitespace = false,
    };

    private string _xml = string.Empty;
    private byte[] _xmlUtf8 = Array.Empty<byte>();

    [Params("Small", "Medium", "Large")]
    public string Size { get; set; } = "Medium";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _xml = BenchmarkData.GetXml(Size);
        _xmlUtf8 = BenchmarkData.GetUtf8(Size);
    }

    [Benchmark]
    public SystemTextXmlDocument ParseWithSystemTextXml() => SystemTextXmlDocument.Parse(_xml);

    [Benchmark]
    public System.Xml.XmlDocument ParseWithSystemXml()
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(_xml);
        return document;
    }

    [Benchmark]
    public XDocument ParseWithXLinq() => XDocument.Parse(_xml, LoadOptions.None);

    [Benchmark]
    public int TokenizeWithUtf8XmlReader()
    {
        var reader = new SystemTextXmlReader(_xmlUtf8);
        var count = 0;

        while (reader.Read())
        {
            count++;
        }

        reader.Dispose();
        return count;
    }

    [Benchmark]
    public int TokenizeWithSystemXmlReader()
    {
        using var stringReader = new StringReader(_xml);
        using var reader = XmlReader.Create(stringReader, ReaderSettings);
        var count = 0;

        while (reader.Read())
        {
            count++;
        }

        return count;
    }
}
