using System.Runtime.CompilerServices;

namespace System.Text.Xml;

/// <summary>
/// Parses UTF-8 XML into a flat MetadataDb for zero-allocation document navigation.
/// </summary>
internal static class XmlMetadataParser
{
    private struct ElementFrame
    {
        public int DbIndex;       // Index in MetadataDb where this element's row lives
        public int ChildCount;    // Running count of direct children
    }

    public static XmlDocument Parse(byte[] sourceBytes)
    {
        var reader = new Utf8XmlReader(
            sourceBytes,
            new XmlReaderOptions
            {
                CommentHandling = XmlCommentHandling.Allow,
                IgnoreWhitespace = true,
            });

        try
        {
            var db = new MetadataDb(Math.Max(64, sourceBytes.Length / 20)); // rough estimate: 1 node per 20 bytes
            var frameStack = new ElementFrame[16];
            int frameCount = 0;
            int rootIndex = -1;
            int declarationIndex = -1;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case XmlTokenType.XmlDeclaration:
                    {
                        var row = DbRow.CreateDeclaration(
                            (int)reader.BytesConsumed - reader.ValueSpan.Length - 2, // approximate start
                            reader.ValueSpan.Length);
                        // Fix: use actual value positions from the reader
                        row.ValueStart = GetSpanStart(sourceBytes, reader.ValueSpan);
                        row.ValueLength = reader.ValueSpan.Length;
                        declarationIndex = db.Append(row);
                        break;
                    }

                    case XmlTokenType.StartElement:
                    {
                        int localNameStart = GetSpanStart(sourceBytes, reader.LocalNameSpan);
                        ushort localNameLength = (ushort)reader.LocalNameSpan.Length;
                        ushort prefixLength = (ushort)reader.PrefixSpan.Length;

                        int nsUriStart = 0;
                        ushort nsUriLength = 0;
                        var nsSpan = reader.NamespaceUriSpan;
                        if (!nsSpan.IsEmpty)
                        {
                            int nsOffset = GetSpanStart(sourceBytes, nsSpan);
                            if (nsOffset >= 0)
                            {
                                nsUriStart = nsOffset;
                                nsUriLength = (ushort)nsSpan.Length;
                            }
                            // else: well-known URI (xml/xmlns) — will be resolved from prefix at access time
                        }

                        var elementRow = DbRow.CreateElement(localNameStart, localNameLength, prefixLength, nsUriStart, nsUriLength);
                        int elementIndex = db.Append(elementRow);

                        if (rootIndex < 0 && frameCount == 0)
                        {
                            rootIndex = elementIndex;
                        }

                        // Parse attributes directly into the db
                        int attrCount = 0;
                        while (reader.MoveToNextAttribute())
                        {
                            int attrLocalNameStart = GetSpanStart(sourceBytes, reader.LocalNameSpan);
                            ushort attrLocalNameLength = (ushort)reader.LocalNameSpan.Length;
                            ushort attrPrefixLength = (ushort)reader.PrefixSpan.Length;
                            int attrValueStart = reader.ValueSpan.IsEmpty ? 0 : GetSpanStart(sourceBytes, reader.ValueSpan);
                            int attrValueLength = reader.ValueSpan.Length;

                            int attrNsUriStart = 0;
                            ushort attrNsUriLength = 0;
                            var attrNsSpan = reader.NamespaceUriSpan;
                            if (!attrNsSpan.IsEmpty)
                            {
                                int attrNsOffset = GetSpanStart(sourceBytes, attrNsSpan);
                                if (attrNsOffset >= 0)
                                {
                                    attrNsUriStart = attrNsOffset;
                                    attrNsUriLength = (ushort)attrNsSpan.Length;
                                }
                            }

                            db.Append(DbRow.CreateAttribute(
                                attrLocalNameStart, attrLocalNameLength, attrPrefixLength,
                                attrValueStart, attrValueLength,
                                attrNsUriStart, attrNsUriLength));
                            attrCount++;
                        }

                        // Update attribute count on element row
                        ref DbRow elemRow = ref db.GetRow(elementIndex);
                        elemRow.AttributeCount = (ushort)attrCount;

                        // Push frame — even for empty elements, since the reader emits a synthetic EndElement
                        if (frameCount == frameStack.Length)
                        {
                            Array.Resize(ref frameStack, frameStack.Length * 2);
                        }
                        frameStack[frameCount++] = new ElementFrame { DbIndex = elementIndex, ChildCount = 0 };

                        break;
                    }

                    case XmlTokenType.EndElement:
                    {
                        if (frameCount > 0)
                        {
                            var frame = frameStack[--frameCount];
                            ref DbRow elemRow = ref db.GetRow(frame.DbIndex);
                            elemRow.EndIndex = db.Count;
                            elemRow.ChildCount = frame.ChildCount;

                            // Increment parent's child count
                            if (frameCount > 0)
                            {
                                frameStack[frameCount - 1].ChildCount++;
                            }
                        }
                        break;
                    }

                    case XmlTokenType.Text:
                    {
                        int valueStart = GetSpanStart(sourceBytes, reader.ValueSpan);
                        db.Append(DbRow.CreateText(valueStart, reader.ValueSpan.Length));
                        if (frameCount > 0) frameStack[frameCount - 1].ChildCount++;
                        break;
                    }

                    case XmlTokenType.CData:
                    {
                        int valueStart = GetSpanStart(sourceBytes, reader.ValueSpan);
                        db.Append(DbRow.CreateCData(valueStart, reader.ValueSpan.Length));
                        if (frameCount > 0) frameStack[frameCount - 1].ChildCount++;
                        break;
                    }

                    case XmlTokenType.Comment:
                    {
                        int valueStart = GetSpanStart(sourceBytes, reader.ValueSpan);
                        db.Append(DbRow.CreateComment(valueStart, reader.ValueSpan.Length));
                        if (frameCount > 0) frameStack[frameCount - 1].ChildCount++;
                        break;
                    }

                    case XmlTokenType.ProcessingInstruction:
                    {
                        int nameStart = GetSpanStart(sourceBytes, reader.LocalNameSpan);
                        int valueStart = reader.ValueSpan.IsEmpty ? 0 : GetSpanStart(sourceBytes, reader.ValueSpan);
                        db.Append(DbRow.CreateProcessingInstruction(
                            nameStart, (ushort)reader.LocalNameSpan.Length,
                            valueStart, reader.ValueSpan.Length));
                        if (frameCount > 0) frameStack[frameCount - 1].ChildCount++;
                        break;
                    }
                }
            }

            if (rootIndex < 0)
            {
                throw new FormatException("The XML payload did not contain a document element.");
            }

            var compactDb = db.Compact();
            return new XmlDocument(sourceBytes, compactDb, rootIndex, declarationIndex);
        }
        finally
        {
            reader.Dispose();
        }
    }

    /// <summary>
    /// Gets the offset of a ReadOnlySpan within the source byte array.
    /// Returns -1 if the span is not within the source array.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetSpanStart(byte[] source, ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty) return 0;
        int offset = (int)Unsafe.ByteOffset(
            ref source[0],
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span));
        // Validate the offset is within bounds (span might be from a static array, not the source)
        if (offset < 0 || offset + span.Length > source.Length)
            return -1;
        return offset;
    }
}
