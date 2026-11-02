using System.IO.Compression;
using System.Text;

namespace StarshipTitanicAp;

/// <summary>A whole save, kept in slot data storage so a new game can restore it: every line of the save's object tree and
/// the game state after it, as a patch against newgame.st in the format of <see cref="QuickStartSave.ApplyPatch"/>,
/// gzipped and in base64. The save's header is left out, as its thumbnail, date and play time aren't game state.
/// Nothing is worked out from AP here, which is the point: fields are to be taken out of this deliberately, later.</summary>
internal static class SaveSnapshot
{
    /// <summary>The data storage key this is kept under.</summary>
    public const string StorageKey = "save_snapshot";

    /// <summary>Makes a snapshot from the game's newgame.st and a save file. Throws InvalidDataException if the save isn't
    /// one, or the snapshot wouldn't restore it exactly.</summary>
    public static string Create(byte[] newGameFile, byte[] saveFile)
    {
        string newGame = QuickStartSave.ReadText(newGameFile);
        string save = QuickStartSave.ReadText(saveFile);
        int tree = save.IndexOf(SaveTree.TreeStart, StringComparison.Ordinal);
        if (tree < 0)
            throw new InvalidDataException("The save has no object tree");
        save = save[tree..];

        string[] oldLines = newGame.Split('\n');
        string[] newLines = save.Split('\n');
        var hunks = new List<string>();
        Diff(oldLines, 0, oldLines.Length, newLines, 0, newLines.Length, hunks);
        string patch = string.Join('\n', hunks);
        if (Apply(newGame, patch) != save)
            throw new InvalidDataException("The snapshot doesn't reproduce the save");

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.Latin1.GetBytes(patch));
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Makes the save file a snapshot came from, without its header. Throws InvalidDataException or FormatException
    /// if it can't be read.</summary>
    public static byte[] Restore(byte[] newGameFile, string snapshot)
    {
        using var gzip = new GZipStream(new MemoryStream(Convert.FromBase64String(snapshot)), CompressionMode.Decompress);
        using var text = new MemoryStream();
        gzip.CopyTo(text);
        return QuickStartSave.WriteText(Apply(QuickStartSave.ReadText(newGameFile), Encoding.Latin1.GetString(text.ToArray())));
    }

    private static string Apply(string newGame, string patch) =>
        patch.Length == 0 ? newGame : QuickStartSave.ApplyPatch(newGame, patch);

    /// <summary>Patience diff: lines found once on each side and in the same order are taken as fixed, and the gaps between
    /// them are compared in turn. A save's object names make good anchors, which leaves most gaps a few lines long.</summary>
    private static void Diff(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd, List<string> hunks)
    {
        while (aStart < aEnd && bStart < bEnd && a[aStart] == b[bStart])
        {
            aStart++;
            bStart++;
        }
        while (aStart < aEnd && bStart < bEnd && a[aEnd - 1] == b[bEnd - 1])
        {
            aEnd--;
            bEnd--;
        }
        if (aStart == aEnd && bStart == bEnd)
            return;

        List<(int A, int B)> anchors = FindAnchors(a, aStart, aEnd, b, bStart, bEnd);
        if (anchors.Count == 0)
        {
            AddGap(a, aStart, aEnd, b, bStart, bEnd, hunks);
            return;
        }

        foreach ((int anchorA, int anchorB) in anchors)
        {
            Diff(a, aStart, anchorA, b, bStart, anchorB, hunks);
            aStart = anchorA + 1;
            bStart = anchorB + 1;
        }
        Diff(a, aStart, aEnd, b, bStart, bEnd, hunks);
    }

    /// <summary>The longest run of lines found once on each side and in the same order on both.</summary>
    private static List<(int A, int B)> FindAnchors(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd)
    {
        Dictionary<string, int> inA = FindUniqueLines(a, aStart, aEnd);
        Dictionary<string, int> inB = FindUniqueLines(b, bStart, bEnd);

        var candidates = new List<(int A, int B)>();
        foreach ((string line, int indexA) in inA)
        {
            if (inB.TryGetValue(line, out int indexB))
                candidates.Add((indexA, indexB));
        }
        candidates.Sort((x, y) => x.A.CompareTo(y.A));

        // Patience sorting: each pile's top is the smallest B that ends a run of its length
        var tops = new List<int>();
        var previous = new int[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            int low = 0, high = tops.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (candidates[tops[middle]].B < candidates[i].B)
                    low = middle + 1;
                else
                    high = middle;
            }
            previous[i] = low > 0 ? tops[low - 1] : -1;
            if (low == tops.Count)
                tops.Add(i);
            else
                tops[low] = i;
        }

        var anchors = new List<(int A, int B)>();
        for (int i = tops.Count > 0 ? tops[^1] : -1; i >= 0; i = previous[i])
            anchors.Add(candidates[i]);
        anchors.Reverse();
        return anchors;
    }

    private static Dictionary<string, int> FindUniqueLines(string[] lines, int start, int end)
    {
        var unique = new Dictionary<string, int>(StringComparer.Ordinal);
        var repeated = new HashSet<string>(StringComparer.Ordinal);
        for (int i = start; i < end; i++)
        {
            if (!repeated.Contains(lines[i]) && !unique.TryAdd(lines[i], i))
            {
                unique.Remove(lines[i]);
                repeated.Add(lines[i]);
            }
        }
        return unique;
    }

    /// <summary>A gap with nothing to anchor on. Most hold an object's changed numbers, with its lines still matching up
    /// one for one, so only those that differ are written. Otherwise the whole gap is replaced.</summary>
    private static void AddGap(string[] a, int aStart, int aEnd, string[] b, int bStart, int bEnd, List<string> hunks)
    {
        if (aEnd - aStart != bEnd - bStart)
        {
            AddHunk(aStart, aEnd - aStart, b, bStart, bEnd, hunks);
            return;
        }

        for (int i = 0; i < aEnd - aStart; i++)
        {
            if (a[aStart + i] != b[bStart + i])
                AddHunk(aStart + i, 1, b, bStart + i, bStart + i + 1, hunks);
        }
    }

    private static void AddHunk(int start, int removed, string[] b, int bStart, int bEnd, List<string> hunks)
    {
        hunks.Add($"{start} {removed} {bEnd - bStart}");
        for (int i = bStart; i < bEnd; i++)
            hunks.Add(b[i]);
    }
}
