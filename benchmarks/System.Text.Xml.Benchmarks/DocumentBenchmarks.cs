using BenchmarkDotNet.Attributes;
using System.IO;
using System.Text.Xml;
using System.Xml.Linq;

namespace System.Text.Xml.Benchmarks;

[MemoryDiagnoser]
public class DocumentBenchmarks
{
    private string _xml = string.Empty;

    [Params("Small", "Medium", "Large")]
    public string Size { get; set; } = "Medium";

    [GlobalSetup]
    public void GlobalSetup()
    {
        _xml = BenchmarkData.GetXml(Size);
    }

    [Benchmark]
    public int NavigateSystemTextXml()
    {
        using var document = XmlDocument.Parse(_xml);
        return TraverseElement(document.Root);
    }

    [Benchmark]
    public int NavigateSystemXml()
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(_xml);
        return document.DocumentElement is null ? 0 : TraverseSystemXmlElement(document.DocumentElement);
    }

    [Benchmark]
    public int NavigateXLinq()
    {
        var document = XDocument.Parse(_xml, LoadOptions.None);
        return document.Root is null ? 0 : TraverseXElement(document.Root);
    }

    [Benchmark]
    public int RoundTripSystemTextXml()
    {
        using var document = XmlDocument.Parse(_xml);
        using var stream = new MemoryStream();
        document.Save(stream);
        return checked((int)stream.Length);
    }

    [Benchmark]
    public int RoundTripSystemXml()
    {
        var document = new System.Xml.XmlDocument();
        document.LoadXml(_xml);
        using var stream = new MemoryStream();
        document.Save(stream);
        return checked((int)stream.Length);
    }

    [Benchmark]
    public int RoundTripXLinq()
    {
        var document = XDocument.Parse(_xml, LoadOptions.None);
        using var stream = new MemoryStream();
        document.Save(stream, SaveOptions.DisableFormatting);
        return checked((int)stream.Length);
    }

    private static int TraverseElement(XmlElementNode element)
    {
        int total = element.LocalName.Length;

        for (int i = 0; i < element.Attributes.Count; i++)
        {
            var attr = element.Attributes[i];
            total += attr.LocalName.Length + attr.Value.Length;
        }

        for (int i = 0; i < element.Children.Count; i++)
        {
            var child = element.Children[i];
            if (child is XmlElementNode childElement)
            {
                total += TraverseElement(childElement);
            }
            else if (child is XmlTextNode text)
            {
                total += text.Value.Length;
            }
            else if (child is XmlCDataNode cdata)
            {
                total += cdata.Value.Length;
            }
        }

        return total;
    }

    private static int TraverseSystemXmlElement(System.Xml.XmlElement element)
    {
        var total = element.LocalName.Length;

        foreach (System.Xml.XmlAttribute attribute in element.Attributes)
        {
            total += attribute.LocalName.Length + attribute.Value.Length;
        }

        foreach (System.Xml.XmlNode child in element.ChildNodes)
        {
            if (child is System.Xml.XmlElement childElement)
            {
                total += TraverseSystemXmlElement(childElement);
            }
            else if (child is System.Xml.XmlText text)
            {
                total += text.Value?.Length ?? 0;
            }
        }

        return total;
    }

    private static int TraverseXElement(XElement element)
    {
        var total = element.Name.LocalName.Length;

        foreach (var attribute in element.Attributes())
        {
            total += attribute.Name.LocalName.Length + attribute.Value.Length;
        }

        foreach (var node in element.Nodes())
        {
            if (node is XElement childElement)
            {
                total += TraverseXElement(childElement);
            }
            else if (node is XText text)
            {
                total += text.Value.Length;
            }
        }

        return total;
    }
}
