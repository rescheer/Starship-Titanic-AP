using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace StarshipTitanicAp;

/// <summary>Builds the save Skip Intro loads: the game's own newgame.st, patched to the moment a vanilla game reaches
/// the Embarkation Lobby before check-in, and tagged with the seed so <see cref="SaveSeedGuard"/> accepts it. The patch
/// comes from tools/make_skip_intro_patch.py and only fits the newgame.st it was made from.</summary>
internal static class SkipIntroSave
{
    private const string PatchResourceName = "SkipIntroPatch.gz";

    /// <summary>SHA-256 of the newgame.st the patch was made from.</summary>
    private const string NewGameSha256 = "B434B527A8A4AEE81ADF1AE0B80DC521E08D34A4B45A3E0F77F243CDEA6A6E21";

    private const string BeamBridgeNameLine = "\n\t\"BeamBridge\" \n\t0 \n\t0 \n\t0 \n}";
    private const string BeamBridgeClassLine = "\"CBridgePiece\"";
    private const string UntaggedFloats = "\t\t0.000000 \n\t\t0.000000 \n\t\t0.000000 \n";

    /// <summary>Whether <paramref name="newGameFile"/> is the newgame.st the patch fits.</summary>
    public static bool FitsPatch(byte[] newGameFile) =>
        Convert.ToHexString(SHA256.HashData(newGameFile)) == NewGameSha256;

    /// <summary>Returns the finished save file. Throws InvalidDataException if the patch can't be applied.</summary>
    public static byte[] Build(byte[] newGameFile, long seedTag)
    {
        string newGame = Encoding.Latin1.GetString(Decompress(new ZLibStream(new MemoryStream(newGameFile), CompressionMode.Decompress)));
        using Stream patch = Assembly.GetExecutingAssembly().GetManifestResourceStream(PatchResourceName)
            ?? throw new InvalidDataException($"{PatchResourceName} is missing from the client");
        string patchText = Encoding.Latin1.GetString(Decompress(new GZipStream(patch, CompressionMode.Decompress)));

        string save = WriteSeedTag(ApplyPatch(newGame, patchText), seedTag);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(Encoding.Latin1.GetBytes(save));
        return output.ToArray();
    }

    private static byte[] Decompress(Stream stream)
    {
        using (stream)
        using (var output = new MemoryStream())
        {
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    /// <summary>Each hunk is a "start removed added" line followed by its added lines, with start counted from 0.</summary>
    internal static string ApplyPatch(string text, string patch)
    {
        string[] lines = text.Split('\n');
        string[] patchLines = patch.Split('\n');
        var output = new List<string>(lines.Length);

        int position = 0;
        int patchIndex = 0;
        while (patchIndex < patchLines.Length)
        {
            string[] hunk = patchLines[patchIndex++].Split(' ');
            if (hunk.Length != 3)
                throw new InvalidDataException($"Bad Skip Intro patch hunk at line {patchIndex}");
            int start = int.Parse(hunk[0]), removed = int.Parse(hunk[1]), added = int.Parse(hunk[2]);
            if (start < position || start + removed > lines.Length || patchIndex + added > patchLines.Length)
                throw new InvalidDataException($"Skip Intro patch hunk at line {patchIndex} is out of range");

            output.AddRange(lines[position..start]);
            output.AddRange(patchLines[patchIndex..(patchIndex + added)]);
            position = start + removed;
            patchIndex += added;
        }
        output.AddRange(lines[position..]);
        return string.Join('\n', output);
    }

    /// <summary>Writes the tag where CGameObject::save writes BeamBridge's _unused3 (see <see cref="SaveSeedTag"/>):
    /// the last of its three floats, formatted as the game's writeFloatLine does.</summary>
    internal static string WriteSeedTag(string save, long seedTag)
    {
        int name = save.IndexOf(BeamBridgeNameLine, StringComparison.Ordinal);
        int block = name < 0 ? -1 : save.LastIndexOf(BeamBridgeClassLine, name, StringComparison.Ordinal);
        int floats = block < 0 ? -1 : save.IndexOf(UntaggedFloats, block, name - block, StringComparison.Ordinal);
        if (floats < 0)
            throw new InvalidDataException("Could not find BeamBridge's seed tag field");

        return string.Concat(
            save.AsSpan(0, floats),
            $"\t\t0.000000 \n\t\t0.000000 \n\t\t{seedTag}.000000 \n",
            save.AsSpan(floats + UntaggedFloats.Length));
    }
}
