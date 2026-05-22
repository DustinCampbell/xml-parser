using System;

namespace System.Text.Xml;

/// <summary>
/// Represents an XML processing instruction.
/// </summary>
public sealed class XmlProcessingInstructionNode : XmlNode
{
    /// <summary>
    /// Initializes a new <see cref="XmlProcessingInstructionNode"/> instance.
    /// </summary>
    /// <param name="target">The processing instruction target.</param>
    /// <param name="data">The optional processing instruction data.</param>
    public XmlProcessingInstructionNode(string target, string? data)
        : base(XmlNodeType.ProcessingInstruction)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("The processing instruction target must not be empty.", nameof(target));
        }

        Target = target;
        Data = data;
    }

    /// <summary>
    /// Gets the processing instruction target.
    /// </summary>
    public string Target { get; }

    /// <summary>
    /// Gets the optional processing instruction data.
    /// </summary>
    public string? Data { get; }

    /// <inheritdoc />
    public override void WriteTo(Utf8XmlWriter writer)
    {
        ThrowHelper.ThrowIfNull(writer);
        WriteTo(new Utf8XmlNodeWriter(writer));
    }

    internal override void WriteTo(XmlNodeWriter writer) => writer.WriteProcessingInstruction(Target, Data);
}