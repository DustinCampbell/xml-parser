using System.Runtime.CompilerServices;
using System.Text;

namespace System.Text.Xml;

/// <summary>
/// A simple open-addressing hash table that caches strings decoded from UTF-8 byte spans.
/// Used by the DOM parser to deduplicate element names, attribute names, prefixes, and namespace URIs.
/// </summary>
internal sealed class Utf8StringCache
{
    private Entry[] _entries;
    private int _count;

    public Utf8StringCache(int initialCapacity = 32)
    {
        _entries = new Entry[initialCapacity];
    }

    /// <summary>
    /// Returns a cached string for the given UTF-8 bytes, or decodes and caches a new one.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string GetOrAdd(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return string.Empty;
        }

        uint hash = ComputeHash(utf8);
        Entry[] entries = _entries;
        uint mask = (uint)(entries.Length - 1);
        uint index = hash & mask;

        while (true)
        {
            ref Entry entry = ref entries[index];
            if (entry.Key is null)
            {
                return AddEntry(ref entry, utf8, hash);
            }

            if (entry.Hash == hash && utf8.SequenceEqual(entry.Key))
            {
                return entry.Value!;
            }

            index = (index + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private string AddEntry(ref Entry slot, ReadOnlySpan<byte> utf8, uint hash)
    {
        byte[] keyBytes = utf8.ToArray();
#if NET
        string value = Encoding.UTF8.GetString(utf8);
#else
        string value = Encoding.UTF8.GetString(keyBytes, 0, keyBytes.Length);
#endif
        slot.Key = keyBytes;
        slot.Hash = hash;
        slot.Value = value;
        _count++;

        if (_count > _entries.Length * 3 / 4)
        {
            Resize();
        }

        return value;
    }

    private void Resize()
    {
        var oldEntries = _entries;
        var newEntries = new Entry[oldEntries.Length * 2];
        uint mask = (uint)(newEntries.Length - 1);

        for (int i = 0; i < oldEntries.Length; i++)
        {
            if (oldEntries[i].Key is not null)
            {
                uint idx = oldEntries[i].Hash & mask;
                while (newEntries[idx].Key is not null)
                {
                    idx = (idx + 1) & mask;
                }

                newEntries[idx] = oldEntries[i];
            }
        }

        _entries = newEntries;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ComputeHash(ReadOnlySpan<byte> data)
    {
        // FNV-1a hash - fast for short byte sequences
        uint hash = 2166136261u;
        for (int i = 0; i < data.Length; i++)
        {
            hash = (hash ^ data[i]) * 16777619u;
        }

        return hash;
    }

    private struct Entry
    {
        public byte[]? Key;
        public uint Hash;
        public string? Value;
    }
}
