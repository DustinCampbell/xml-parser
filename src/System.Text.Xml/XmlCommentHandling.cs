namespace System.Text.Xml;

/// <summary>
/// Defines how comments are handled when reading XML.
/// </summary>
public enum XmlCommentHandling
{
    /// <summary>Skips comment nodes.</summary>
    Skip,

    /// <summary>Returns comment nodes as tokens.</summary>
    Allow,
}