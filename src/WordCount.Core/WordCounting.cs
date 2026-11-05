using System.Runtime.InteropServices;
using System.Text;

namespace WordCount.Core;

/// <summary>A half-open byte range [Begin, End) of the input file.</summary>
public readonly record struct ByteRange(long Begin, long End)
{
    public long Length => End - Begin;
}

/// <summary>What one scan of a byte range produced, besides the counts themselves.</summary>
public readonly record struct ScanStats(long BytesRead, long Words);

/// <summary>
/// Word-frequency counting with the semantics of the RPP lab (C++ wf-benchmark):
/// a word is a maximal run of ASCII letters or digits, lowercased; every other byte is a
/// delimiter. A range owns exactly the words that <em>start</em> inside it, so any split
/// of the file into contiguous ranges counts every word exactly once.
/// </summary>
public static class WordCounting
{
    private const int BlockSize = 1 << 20;

    public static bool IsWordByte(byte b) =>
        (uint)(b - '0') < 10 || (uint)((b | 0x20) - 'a') < 26;

    /// <summary>Splits [0, size) into <paramref name="count"/> contiguous ranges; the first
    /// size % count ranges get one extra byte (same as build_even_ranges in RPP).</summary>
    public static ByteRange[] EvenRanges(long size, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        var ranges = new ByteRange[count];
        long baseSize = size / count, extra = size % count, begin = 0;
        for (int i = 0; i < count; i++)
        {
            long end = begin + baseSize + (i < extra ? 1 : 0);
            ranges[i] = new ByteRange(begin, end);
            begin = end;
        }
        return ranges;
    }

    public static Dictionary<string, long> NewCounts() => new(StringComparer.Ordinal);

    /// <summary>Counts the words that start in <paramref name="range"/> of the file at
    /// <paramref name="path"/> into <paramref name="counts"/>. Reads past the end of the range
    /// only as far as needed to finish its last word.</summary>
    public static ScanStats CountRange(string path, ByteRange range, Dictionary<string, long> counts)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1, FileOptions.SequentialScan);
        long fileLength = file.Length;
        long begin = Math.Clamp(range.Begin, 0, fileLength);
        long end = Math.Clamp(range.End, begin, fileLength);
        if (begin == end) return new ScanStats(0, 0);

        // A word that is already running at `begin` belongs to the previous range.
        bool skipping = false;
        if (begin > 0)
        {
            file.Position = begin - 1;
            int previous = file.ReadByte();
            skipping = previous >= 0 && IsWordByte((byte)previous);
        }
        file.Position = begin;

        var lookup = counts.GetAlternateLookup<ReadOnlySpan<char>>();
        var buffer = new byte[BlockSize];
        var word = new char[256];
        int wordLength = 0;
        bool inWord = false;
        long position = begin, bytesRead = 0, words = 0;

        while (true)
        {
            int read = file.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            bytesRead += read;

            for (int i = 0; i < read; i++, position++)
            {
                byte b = buffer[i];
                if (IsWordByte(b))
                {
                    if (skipping) continue;
                    if (!inWord)
                    {
                        if (position >= end) goto done;   // starts in the next range
                        inWord = true;
                        wordLength = 0;
                    }
                    if (wordLength == word.Length) Array.Resize(ref word, word.Length * 2);
                    word[wordLength++] = (char)((uint)(b - 'A') < 26 ? b | 0x20 : b);   // ASCII lowercase
                }
                else
                {
                    skipping = false;
                    if (inWord)
                    {
                        Add(lookup, word.AsSpan(0, wordLength));
                        words++;
                        inWord = false;
                    }
                    if (position >= end) goto done;
                }
            }
        }

        if (inWord)   // the file ended inside a word
        {
            Add(lookup, word.AsSpan(0, wordLength));
            words++;
        }

    done:
        return new ScanStats(bytesRead, words);
    }

    private static void Add(Dictionary<string, long>.AlternateLookup<ReadOnlySpan<char>> lookup, ReadOnlySpan<char> word)
    {
        ref long slot = ref CollectionsMarshal.GetValueRefOrAddDefault(lookup, word, out _);
        slot++;
    }

    /// <summary>Adds every count of <paramref name="part"/> into <paramref name="total"/>.</summary>
    public static void MergeInto(Dictionary<string, long> total, IReadOnlyDictionary<string, long> part)
    {
        foreach (var (word, count) in part)
        {
            ref long slot = ref CollectionsMarshal.GetValueRefOrAddDefault(total, word, out _);
            slot += count;
        }
    }

    /// <summary>The RPP output contract: one "word count" line per entry, in ordinal
    /// (byte-wise) word order, which equals std::map order for ASCII words.</summary>
    public static byte[] FormatFrequencies(Dictionary<string, long> counts)
    {
        var words = counts.Keys.ToArray();
        Array.Sort(words, StringComparer.Ordinal);
        var builder = new StringBuilder(words.Length * 16);
        foreach (var word in words)
        {
            builder.Append(word).Append(' ').Append(counts[word]).Append('\n');
        }
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
