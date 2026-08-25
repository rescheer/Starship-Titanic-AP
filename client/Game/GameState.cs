namespace StarshipTitanicAp;

public readonly record struct RoomNodeView(int Room, int Node, int View);

public readonly record struct InventoryItem(long Address, string? Name);

/// <summary>One resolved carryable item, wherever it currently is in the game's tree.</summary>
public readonly record struct CarryItemLocation(string Name, long Address, long? ParentAddress, string? ParentName);

public enum PassengerClass
{
    First = 1,
    Second = 2,
    Third = 3,
    None = 4,
}

public static class PassengerClassNames
{
    public static string GetName(int rawValue) => rawValue switch
    {
        1 => "First Class",
        2 => "Second Class",
        3 => "Third Class",
        4 => "No Class",
        _ => $"(unknown class {rawValue})",
    };
}

/// <summary>
/// Resolves live game state from the process's memory, using the
/// confirmed offset chain in GameOffsets. Every method returns null (or
/// an empty result) rather than throwing when a hop isn't currently
/// readable - e.g. the player is at a menu, mid-load, or between states.
/// </summary>
public static class GameState
{
    private const int MaxTreeNodes = 20000;
    private const int MaxDepthToScan = 3;

    public static long? ResolveGameManager(MemoryReader mem)
    {
        long? step1 = mem.ReadInt64(mem.ModuleBase + GameOffsets.Step1);
        if (step1 is null or 0) return null;

        long? step2 = mem.ReadInt64(step1.Value + GameOffsets.Step2);
        if (step2 is null or 0) return null;

        long? gameManager = mem.ReadInt64(step2.Value + GameOffsets.GameManager);
        if (gameManager is null or 0) return null;

        return gameManager;
    }

    public static RoomNodeView? ReadRoomNodeView(MemoryReader mem, long gameManager)
    {
        int? room = mem.ReadInt32(gameManager + GameOffsets.RoomFromGameManager);
        int? node = mem.ReadInt32(gameManager + GameOffsets.NodeFromGameManager);
        int? view = mem.ReadInt32(gameManager + GameOffsets.ViewFromGameManager);

        if (room is null || node is null || view is null)
            return null;

        return new RoomNodeView(room.Value, node.Value, view.Value);
    }

    public static long? ResolveProject(MemoryReader mem, long gameManager)
    {
        long? project = mem.ReadInt64(gameManager + GameOffsets.Project);
        return project is null or 0 ? null : project;
    }

    public static int? ReadPassengerClass(MemoryReader mem, long gameManager) =>
        mem.ReadInt32(gameManager + GameOffsets.PassengerClass);

    public static bool? ReadPetActive(MemoryReader mem, long gameManager)
    {
        // Stored as a single byte; read via ReadBytes rather than ReadInt32
        // since we only confirmed the flip at this exact byte offset, not
        // that the surrounding 3 bytes are meaningfully part of the same field.
        byte[]? raw = mem.ReadBytes(gameManager + GameOffsets.PetActive, 1);
        return raw is null ? null : raw[0] != 0;
    }

    private static bool LooksLikeHeapPointer(long value) =>
        value > 0x1000 && value < 0x0000800000000000;

    /// <summary>
    /// Scans a node's name-pointer window for a pointer to a short
    /// printable-ASCII string. Mirrors the heuristic used throughout the
    /// original Python tooling - CNamedItem's exact CString layout was
    /// never pinned down precisely, so this scans a range instead.
    /// </summary>
    public static string? TryReadName(MemoryReader mem, long nodeAddress)
    {
        for (long offset = GameOffsets.NameScanStart; offset < GameOffsets.NameScanEnd; offset += GameOffsets.NameScanStep)
        {
            long? candidatePtr = mem.ReadInt64(nodeAddress + offset);
            if (candidatePtr is null || !LooksLikeHeapPointer(candidatePtr.Value))
                continue;

            string? text = mem.ReadShortAsciiString(candidatePtr.Value, 32);
            if (text is { Length: >= 2 and <= 31 })
                return text;
        }
        return null;
    }

    public static List<InventoryItem> ListChildren(MemoryReader mem, long parentAddress, int limit = 100)
    {
        var items = new List<InventoryItem>();
        long? child = mem.ReadInt64(parentAddress + GameOffsets.FirstChild);
        if (child is null)
            return items;

        var seen = new HashSet<long>();
        long current = child.Value;

        while (LooksLikeHeapPointer(current) && seen.Add(current) && items.Count < limit)
        {
            string? name = TryReadName(mem, current);
            items.Add(new InventoryItem(current, name));

            long? next = mem.ReadInt64(current + GameOffsets.NextSibling);
            if (next is null)
                break;
            current = next.Value;
        }

        return items;
    }

    /// <summary>
    /// Finds the container holding exactly three "NoName" children and
    /// returns all three addresses, in forward-sibling order. Index 0 is
    /// confirmed (via extensive live testing) to be CPetControl/inventory.
    /// Index 2 has been observed (via mail testing) to be CMailMan. Index 1
    /// is CStarControl - confirmed via a full tree dump ("dump" debug
    /// console command), which shows all three as siblings under the same
    /// CDontSaveFileItem: CPetControl NoName, CStarControl NoName,
    /// CMailMan NoName, in that order. Not currently used by this app.
    /// </summary>
    public static List<long>? FindNoNameSiblings(MemoryReader mem, long project)
    {
        var visited = new HashSet<long>();
        var stack = new Stack<(long Addr, int Depth)>();
        stack.Push((project, 0));
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxTreeNodes)
        {
            (long addr, int depth) = stack.Pop();
            if (addr == 0 || depth > MaxDepthToScan || !visited.Add(addr))
                continue;
            nodeCount++;

            string? name = TryReadName(mem, addr);

            if (name is null && depth >= 1)
            {
                List<InventoryItem> children = ListChildren(mem, addr);
                List<InventoryItem> noNameChildren = children
                    .Where(c => c.Name is null or "NoName")
                    .ToList();

                if (noNameChildren.Count == 3 && children.Count == 3)
                    return noNameChildren.Select(c => c.Address).ToList();
            }

            long? firstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);

            if (firstChild is not null && LooksLikeHeapPointer(firstChild.Value))
                stack.Push((firstChild.Value, depth + 1));
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push((nextSibling.Value, depth));
        }

        return null;
    }

    /// <summary>
    /// Finds the container holding exactly three "NoName" children and
    /// returns the address of the first one (index 0) - confirmed live
    /// against real pickups/drops. See list_inventory.py for the original
    /// derivation of why index 0 is the right one to trust.
    /// </summary>
    public static long? FindInventoryRoom(MemoryReader mem, long project)
    {
        List<long>? siblings = FindNoNameSiblings(mem, project);
        return siblings is { Count: 3 } ? siblings[0] : null;
    }

    /// <summary>
    /// Same container as FindInventoryRoom, but index 2 (CMailMan) - see
    /// FindNoNameSiblings for the caveat on index reliability.
    /// </summary>
    public static long? FindMailManRoom(MemoryReader mem, long project)
    {
        List<long>? siblings = FindNoNameSiblings(mem, project);
        return siblings is { Count: 3 } ? siblings[2] : null;
    }

    // A full-game-tree walk visits far more nodes than the shallow
    // NoName-siblings search above, so it gets its own, much higher budget.
    private const int MaxItemTreeNodes = 200000;

    /// <summary>
    /// Walks the ENTIRE game tree from _project (no depth limit, unlike
    /// FindNoNameSiblings) looking for any node whose name matches one of
    /// the 40 known carryable items (ItemNames). Mirrors what the game's
    /// own debug console does (Debugger::cmdItem -> findByName), just done
    /// as a single sweep instead of 40 separate by-name searches, and
    /// using only the same FirstChild/NextSibling/Parent primitives
    /// already confirmed elsewhere - no new offsets required.
    ///
    /// This is a MUCH heavier walk than anything else in this file - call
    /// it sparingly (manual refresh, or a slow polling interval), not on
    /// every tick.
    /// </summary>
    public static List<CarryItemLocation> FindAllCarryItems(MemoryReader mem, long project)
    {
        var results = new List<CarryItemLocation>();
        var visited = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(project);
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxItemTreeNodes)
        {
            long addr = stack.Pop();
            if (addr == 0 || !visited.Add(addr))
                continue;
            nodeCount++;

            string? name = TryReadName(mem, addr);
            if (name is not null && ItemNames.IsKnownItemName(name))
            {
                long? parent = mem.ReadInt64(addr + GameOffsets.Parent);
                string? parentName = parent is long p && LooksLikeHeapPointer(p)
                    ? TryReadName(mem, p)
                    : null;
                results.Add(new CarryItemLocation(name, addr, parent, parentName));
            }

            long? firstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);

            if (firstChild is not null && LooksLikeHeapPointer(firstChild.Value))
                stack.Push(firstChild.Value);
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push(nextSibling.Value);
        }

        return results;
    }

    public readonly record struct MailItem(long Address, string Name, bool IsPendingMail, uint DestRoomFlags, uint RoomFlags);

    /// <summary>
    /// Reads every item currently parented under CMailMan, along with
    /// their mail-routing fields.
    /// </summary>
    public static List<MailItem> ReadMailItems(MemoryReader mem, long mailManAddr)
    {
        var result = new List<MailItem>();
        List<InventoryItem> children = ListChildren(mem, mailManAddr, limit: 200);

        foreach (InventoryItem child in children)
        {
            int? pending = mem.ReadInt32(child.Address + GameOffsets.ItemIsPendingMail);
            int? dest = mem.ReadInt32(child.Address + GameOffsets.ItemDestRoomFlags);
            int? room = mem.ReadInt32(child.Address + GameOffsets.ItemRoomFlags);

            if (pending is null || dest is null || room is null)
                continue;

            result.Add(new MailItem(
                child.Address,
                string.IsNullOrEmpty(child.Name) ? "(unnamed)" : child.Name!,
                pending.Value != 0,
                unchecked((uint)dest.Value),
                unchecked((uint)room.Value)));
        }

        return result;
    }
}
