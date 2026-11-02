using System.Text;

namespace StarshipTitanicAp;

/// <summary>One object in a <see cref="SaveTree"/>: its block of text, exactly as the game wrote it, and its place in
/// the tree.</summary>
internal sealed class SaveObject
{
    private const string ResourceKeyLine = "\"Resource Key...\"";

    /// <summary>Lines from CGameObject::save's resource key back to _unused4: the key's version, _roomFlags,
    /// _destRoomFlags, _isPendingMail and _visible are in between.</summary>
    private const int ResourceKeyToUnused4 = 6;
    private const int ResourceKeyToVisible = 5;

    public required string ClassName { get; init; }
    public required string Name { get; init; }
    public required string Text { get; set; }
    public SaveObject? Parent { get; set; }
    public List<SaveObject> Children { get; } = new();

    /// <summary>Writes the value of a number line <paramref name="fieldIndex"/> lines after the class name, in the
    /// order the object's save() writes them. Throws InvalidDataException if that line isn't a number.</summary>
    public void SetNumberField(int fieldIndex, long value)
    {
        string[] lines = Text.Split('\n');
        SetNumberLine(lines, FindClassLine(lines) + 1 + fieldIndex, value);
    }

    /// <summary>Writes the value of a string line, like <see cref="SetNumberField"/> does a number's. The value isn't
    /// escaped. Throws InvalidDataException if that line isn't a string.</summary>
    public void SetStringField(int fieldIndex, string value)
    {
        string[] lines = Text.Split('\n');
        int index = FindClassLine(lines) + 1 + fieldIndex;
        if (index >= lines.Length || !lines[index].TrimStart().StartsWith('"'))
            throw new InvalidDataException($"{ClassName} {Name} has no string on line {index}");

        lines[index] = $"{IndentOf(lines[index])}\"{value}\" ";
        Text = string.Join('\n', lines);
    }

    /// <summary>Writes CGameObject's _unused4, this app's per-item state (see <see cref="ItemPersistedState"/>).</summary>
    public void SetPersistedState(ItemPersistedState state) =>
        SetLineBeforeResourceKey(ResourceKeyToUnused4, state.Encode());

    /// <summary>Writes CGameObject's _visible.</summary>
    public void SetVisible(bool visible) =>
        SetLineBeforeResourceKey(ResourceKeyToVisible, visible ? 1 : 0);

    private void SetLineBeforeResourceKey(int linesBefore, long value)
    {
        string[] lines = Text.Split('\n');
        int resourceKey = Array.FindLastIndex(lines, line => line.Trim() == ResourceKeyLine);
        if (resourceKey < linesBefore)
            throw new InvalidDataException($"{ClassName} {Name} has no resource key line");
        SetNumberLine(lines, resourceKey - linesBefore, value);
    }

    private int FindClassLine(string[] lines)
    {
        int classLine = Array.FindIndex(lines, line => line.Trim() == $"\"{ClassName}\"");
        return classLine >= 0 ? classLine : throw new InvalidDataException($"{ClassName} {Name} has no class name line");
    }

    private static string IndentOf(string line) => line[..(line.Length - line.TrimStart().Length)];

    private void SetNumberLine(string[] lines, int index, long value)
    {
        if (index >= lines.Length || !long.TryParse(lines[index].Trim(), out _))
            throw new InvalidDataException($"{ClassName} {Name} has no number on line {index}");

        lines[index] = $"{IndentOf(lines[index])}{value} ";
        Text = string.Join('\n', lines);
    }
}

/// <summary>A save's object tree, parsed so objects can be found by name, edited and moved. Text outside the edits stays
/// exactly as the game wrote it.</summary>
internal sealed class SaveTree
{
    public const string TreeStart = "\n{\n\t\"CProjectItem\"";

    /// <summary>Positions in CGameManager::save's numbers, which directly follow the tree.</summary>
    public const int StatePassengerClass = 2;
    public const int StatePriorClass = 3;
    public const int StateRoom = 7;
    public const int StateNode = 8;
    public const int StateView = 9;

    private const string DownMarker = "DOWN";
    private const string UpMarker = "UP";
    private const string AlongMarker = "ALONG";

    private readonly string _preamble;
    private readonly List<SaveObject> _roots;
    private readonly Dictionary<string, string> _markerText = new();
    private string _tail;

    private SaveTree(string text)
    {
        int start = text.IndexOf(TreeStart, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidDataException("The save has no object tree");
        _preamble = text[..start];

        int position = start;
        _roots = ParseSiblings(text, ref position, parent: null);
        if (_roots.Count == 0)
            throw new InvalidDataException("The save's object tree is empty");
        _tail = text[position..];
    }

    /// <summary>Parses a decompressed save. Throws InvalidDataException if it isn't one, or if it wouldn't be written
    /// back exactly as it was read.</summary>
    public static SaveTree Parse(string text)
    {
        var tree = new SaveTree(text);
        if (tree.Render() != text)
            throw new InvalidDataException("The save didn't parse back to the same text");
        return tree;
    }

    /// <summary>The single object with this class and name. Throws InvalidDataException if there isn't exactly one.
    /// </summary>
    public SaveObject Find(string className, string name)
    {
        SaveObject[] found = All().Where(item => item.ClassName == className && item.Name == name).ToArray();
        return found.Length == 1 ? found[0]
            : throw new InvalidDataException($"The save has {found.Length} {className} objects named {name}");
    }

    /// <summary>The single object with this name, whatever its class. Throws InvalidDataException if there isn't
    /// exactly one.</summary>
    public SaveObject FindByName(string name)
    {
        SaveObject[] found = All().Where(item => item.Name == name).ToArray();
        return found.Length == 1 ? found[0]
            : throw new InvalidDataException($"The save has {found.Length} objects named {name}");
    }

    /// <summary>Every object of this class.</summary>
    public IEnumerable<SaveObject> FindAllOfClass(string className) => All().Where(item => item.ClassName == className);

    /// <summary>Moves an object, along with its children, to the end of another's children, where the game's own
    /// attach() puts one.</summary>
    public static void Move(SaveObject item, SaveObject newParent)
    {
        item.Parent?.Children.Remove(item);
        newParent.Children.Add(item);
        item.Parent = newParent;
    }

    /// <summary>Writes one of the numbers following the tree (see the State constants).</summary>
    public void SetStateNumber(int index, long value)
    {
        int position = 0;
        for (int i = 0; ; i++)
        {
            while (position < _tail.Length && char.IsWhiteSpace(_tail[position]))
                position++;
            int end = position;
            while (end < _tail.Length && !char.IsWhiteSpace(_tail[end]))
                end++;
            if (!long.TryParse(_tail.AsSpan(position, end - position), out _))
                throw new InvalidDataException($"The save's game state has no number at position {i}");

            if (i == index)
            {
                _tail = string.Concat(_tail.AsSpan(0, position), value.ToString(), _tail.AsSpan(end));
                return;
            }
            position = end;
        }
    }

    public string Render()
    {
        var output = new StringBuilder(_preamble);
        RenderSiblings(_roots, output);
        output.Append(_tail);
        return output.ToString();
    }

    private void RenderSiblings(List<SaveObject> siblings, StringBuilder output)
    {
        foreach (SaveObject item in siblings)
        {
            output.Append(item.Text);
            if (item.Children.Count == 0)
            {
                output.Append(_markerText[AlongMarker]);
                continue;
            }
            output.Append(_markerText[DownMarker]);
            RenderSiblings(item.Children, output);
            output.Append(_markerText[UpMarker]);
        }
    }

    private IEnumerable<SaveObject> All()
    {
        var stack = new Stack<SaveObject>(_roots.AsEnumerable().Reverse());
        while (stack.Count > 0)
        {
            SaveObject item = stack.Pop();
            yield return item;
            for (int i = item.Children.Count - 1; i >= 0; i--)
                stack.Push(item.Children[i]);
        }
    }

    /// <summary>Reads objects until an UP block, which is left unread for the caller, or the end of the tree. As in
    /// CProjectItem::saveData, each object is followed by an ALONG block, or by a DOWN block, its children and an UP
    /// block.</summary>
    private List<SaveObject> ParseSiblings(string text, ref int position, SaveObject? parent)
    {
        var siblings = new List<SaveObject>();
        while (position + 1 < text.Length && text[position] == '\n' && text[position + 1] == '{')
        {
            int end = FindBlockEnd(text, position, out string firstString, out string lastString);
            if (firstString == UpMarker)
                break;

            var item = new SaveObject
            {
                ClassName = firstString,
                Name = lastString == firstString ? "" : lastString,
                Text = text[position..end],
                Parent = parent,
            };
            siblings.Add(item);
            position = end;

            string marker = ReadMarker(text, ref position);
            if (marker == DownMarker)
            {
                item.Children.AddRange(ParseSiblings(text, ref position, item));
                if (ReadMarker(text, ref position) != UpMarker)
                    throw new InvalidDataException($"The children of {item.ClassName} {item.Name} have no UP block");
            }
            else if (marker != AlongMarker)
            {
                throw new InvalidDataException($"Unexpected {marker} block after {item.ClassName} {item.Name}");
            }
        }
        return siblings;
    }

    private string ReadMarker(string text, ref int position)
    {
        int end = FindBlockEnd(text, position, out string marker, out _);
        if (marker is not (DownMarker or UpMarker or AlongMarker))
            throw new InvalidDataException($"Expected a DOWN, UP or ALONG block in the save, not {marker}");

        _markerText.TryAdd(marker, text[position..end]);
        position = end;
        return marker;
    }

    /// <summary>The position just past the block starting at <paramref name="start"/>, and the first and last strings in
    /// it, which for an object are its class and name. A block runs from a line end and its opening brace to its closing
    /// brace and a line end. Strings may hold escaped quotes and braces (SimpleFile::writeString).</summary>
    private static int FindBlockEnd(string text, int start, out string firstString, out string lastString)
    {
        firstString = lastString = "";

        int depth = 0;
        for (int position = start; position < text.Length; position++)
        {
            char c = text[position];
            if (c == '"')
            {
                int close = position + 1;
                while (close < text.Length && text[close] != '"')
                    close += text[close] == '\\' ? 2 : 1;
                if (close >= text.Length)
                    break;

                lastString = text[(position + 1)..close];
                if (firstString.Length == 0)
                    firstString = lastString;
                position = close;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                if (position + 1 >= text.Length || text[position + 1] != '\n')
                    throw new InvalidDataException($"The block at position {start} of the save has no line end");
                return position + 2;
            }
        }
        throw new InvalidDataException("The save's object tree ended early");
    }
}
