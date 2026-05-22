using System;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// Represents an XML declaration such as <c>&lt;?xml version="1.0" encoding="utf-8"?&gt;</c>.
/// </summary>
public sealed class XmlDeclarationNode : XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlDeclarationNode"/> instance.
    /// </summary>
    /// <param name="version">The XML version. The default is <c>"1.0"</c>.</param>
    /// <param name="encoding">The declared encoding. The default is <c>"utf-8"</c>.</param>
    /// <param name="standalone">The optional standalone value, such as <c>"yes"</c> or <c>"no"</c>.</param>
    public XmlDeclarationNode(string version = "1.0", string? encoding = "utf-8", string? standalone = null)
        : base(XmlNodeType.Declaration)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("The XML version must not be empty.", nameof(version));
        }

        Version = version;
        Encoding = encoding;
        Standalone = standalone;
    }

    /// <summary>
    /// Gets the XML version.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the declared encoding.
    /// </summary>
    public string? Encoding { get; }

    /// <summary>
    /// Gets the standalone value.
    /// </summary>
    public string? Standalone { get; }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ThrowHelper.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer) => writer.WriteDeclaration(Version, Encoding, Standalone);

    internal static string FormatDeclaration(string version, string? encoding, string? standalone)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=");
        builder.Append('"');
        builder.Append(global::System.Security.SecurityElement.Escape(version));
        builder.Append('"');

        if (!string.IsNullOrEmpty(encoding))
        {
            builder.Append(" encoding=");
            builder.Append('"');
            builder.Append(global::System.Security.SecurityElement.Escape(encoding));
            builder.Append('"');
        }

        if (!string.IsNullOrEmpty(standalone))
        {
            builder.Append(" standalone=");
            builder.Append('"');
            builder.Append(global::System.Security.SecurityElement.Escape(standalone));
            builder.Append('"');
        }

        builder.Append("?>");
        return builder.ToString();
    }
}