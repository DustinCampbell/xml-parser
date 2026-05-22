namespace System.Text.Xml;

/// <summary>
/// Defines the level of conformance enforced by the reader or writer.
/// </summary>
public enum XmlConformanceLevel
{
    /// <summary>Requires a single top-level document element.</summary>
    Document,

    /// <summary>Allows multiple top-level nodes.</summary>
    Fragment,
}