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

/// <summary>The passenger-class value used for the Bridge-access grant after Titania's repair.</summary>
public static class SpecialPassengerClassValues
{
    public const int BridgeAccessClassValue = 4;
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

/// <summary>Resolves live game state from the process's memory.</summary>
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

    /// <summary>Reads the current room's roomFlags value directly off the PET control.</summary>
    public static uint? ReadCurrentRoomFlags(MemoryReader mem, long petControlAddr)
    {
        int? raw = mem.ReadInt32(petControlAddr + GameOffsets.PetControlCurrentRoomFlags);
        return raw is null ? null : unchecked((uint)raw.Value);
    }

    public static bool? ReadPetActive(MemoryReader mem, long gameManager)
    {
        byte[]? raw = mem.ReadBytes(gameManager + GameOffsets.PetActive, 1);
        return raw is null ? null : raw[0] != 0;
    }

    private static bool LooksLikeHeapPointer(long value) =>
        value > 0x1000 && value < 0x0000800000000000;

    /// <summary>Scans a node's name-pointer window for a pointer to a short printable-ASCII string.</summary>
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

    /// <summary>Finds the container holding exactly three "NoName" children and returns all three addresses, in sibling order.</summary>
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

    /// <summary>Finds the container holding exactly three "NoName" children and returns the first one's address (the inventory room).</summary>
    public static long? FindInventoryRoom(MemoryReader mem, long project)
    {
        List<long>? siblings = FindNoNameSiblings(mem, project);
        return siblings is { Count: 3 } ? siblings[0] : null;
    }

    /// <summary>Resolves CPetConversations' address from the PET control's own address.</summary>
    public static long ResolveConversationsAddr(long petControlAddr) =>
        petControlAddr + GameOffsets.PetConversationsFieldOffset;

    /// <summary>Reads which PET tab is currently visible.</summary>
    public static int? GetCurrentPetArea(MemoryReader mem, long petControlAddr) =>
        mem.ReadInt32(petControlAddr + GameOffsets.PetControlCurrentAreaOffset);

    /// <summary>Same container as FindInventoryRoom, but the third ("NoName") sibling (CMailMan).</summary>
    public static long? FindMailManRoom(MemoryReader mem, long project)
    {
        List<long>? siblings = FindNoNameSiblings(mem, project);
        return siblings is { Count: 3 } ? siblings[2] : null;
    }

    private const int MaxItemTreeNodes = 200000;

    /// <summary>Walks the entire game tree from _project looking for any node whose name matches a known carryable item.</summary>
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

    private static readonly Dictionary<long, string> ClassNameCache = new();

    /// <summary>Resolves an object's C++ class name via a virtual call through its vtable.</summary>
    public static string? TryGetClassName(MemoryReader mem, long objAddr)
    {
        long? vtable = mem.ReadInt64(objAddr);
        if (vtable is null or 0)
            return null;

        if (ClassNameCache.TryGetValue(vtable.Value, out string? cached))
            return cached;

        long? funcPtr = mem.ReadInt64(vtable.Value);
        if (funcPtr is null or 0)
            return null;

        long? descriptorAddr = RemoteCaller.CallAndGetResult(mem, funcPtr.Value, rcx: objAddr);
        if (descriptorAddr is null or 0)
            return null;

        long? namePtr = mem.ReadInt64(descriptorAddr.Value + 0x08);
        if (namePtr is null or 0)
            return null;

        string? name = mem.ReadShortAsciiString(namePtr.Value, 32);
        if (string.IsNullOrEmpty(name))
            return null;

        ClassNameCache[vtable.Value] = name;
        return name;
    }

    /// <summary>Clears TryGetClassName's cache.</summary>
    public static void ClearClassNameCache() => ClassNameCache.Clear();

    private const int MaxRoomSearchNodes = 5000;

    /// <summary>Finds a room by its exact display name by walking down from _project.</summary>
    public static long? FindRoomByName(MemoryReader mem, long project, string roomName)
    {
        var visited = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(project);
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxRoomSearchNodes)
        {
            long addr = stack.Pop();
            if (addr == 0 || !visited.Add(addr))
                continue;
            nodeCount++;

            if (TryGetClassName(mem, addr) == "CRoomItem")
            {
                string? name = TryReadName(mem, addr);
                if (string.Equals(name, roomName, StringComparison.OrdinalIgnoreCase))
                    return addr;
                continue;
            }

            long? firstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);
            if (firstChild is not null && LooksLikeHeapPointer(firstChild.Value))
                stack.Push(firstChild.Value);
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push(nextSibling.Value);
        }

        return null;
    }

    /// <summary>Returns the address of the n-th (1-indexed) direct child of parentAddr whose class is exactly className.</summary>
    public static long? NthChildOfClass(MemoryReader mem, long parentAddr, string className, int n)
    {
        long? child = mem.ReadInt64(parentAddr + GameOffsets.FirstChild);
        if (child is null)
            return null;

        var seen = new HashSet<long>();
        long current = child.Value;
        int count = 0;

        while (LooksLikeHeapPointer(current) && seen.Add(current))
        {
            if (TryGetClassName(mem, current) == className)
            {
                count++;
                if (count == n)
                    return current;
            }

            long? next = mem.ReadInt64(current + GameOffsets.NextSibling);
            if (next is null)
                break;
            current = next.Value;
        }

        return null;
    }

    private const int MaxDescendantSearchNodes = 2000;

    /// <summary>Bounded search of rootAddr's own subtree for the first node matching className, optionally also requiring an exact name match.</summary>
    public static long? FindDescendant(MemoryReader mem, long rootAddr, string? name, string className)
    {
        long? firstChild = mem.ReadInt64(rootAddr + GameOffsets.FirstChild);
        if (firstChild is null || !LooksLikeHeapPointer(firstChild.Value))
            return null;

        var visited = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(firstChild.Value);
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxDescendantSearchNodes)
        {
            long addr = stack.Pop();
            if (addr == 0 || !visited.Add(addr))
                continue;
            nodeCount++;

            if (TryGetClassName(mem, addr) == className
                && (name is null || string.Equals(TryReadName(mem, addr), name, StringComparison.OrdinalIgnoreCase)))
            {
                return addr;
            }

            long? childFirstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);
            if (childFirstChild is not null && LooksLikeHeapPointer(childFirstChild.Value))
                stack.Push(childFirstChild.Value);
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push(nextSibling.Value);
        }

        return null;
    }

    /// <summary>Bounded search of rootAddr's own subtree for every node matching className (unlike FindDescendant, which stops at the first).</summary>
    public static List<long> FindAllDescendants(MemoryReader mem, long rootAddr, string className)
    {
        var results = new List<long>();
        long? firstChild = mem.ReadInt64(rootAddr + GameOffsets.FirstChild);
        if (firstChild is null || !LooksLikeHeapPointer(firstChild.Value))
            return results;

        var visited = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(firstChild.Value);
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxDescendantSearchNodes)
        {
            long addr = stack.Pop();
            if (addr == 0 || !visited.Add(addr))
                continue;
            nodeCount++;

            if (TryGetClassName(mem, addr) == className)
                results.Add(addr);

            long? childFirstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);
            if (childFirstChild is not null && LooksLikeHeapPointer(childFirstChild.Value))
                stack.Push(childFirstChild.Value);
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push(nextSibling.Value);
        }

        return results;
    }

    /// <summary>Scans a byte range on an object for embedded CString structures (int32 size @ off, int64 data-ptr @ off+8),
    /// to help discover unknown field offsets live - e.g. CShipSetting's _itemName/_frameTarget. Read-only, diagnostic only.</summary>
    public static List<(long Offset, int Size, string Text)> ScanForCStrings(MemoryReader mem, long addr, long startOffset, long endOffset)
    {
        var results = new List<(long, int, string)>();
        for (long off = startOffset; off <= endOffset; off += 4)
        {
            int? size = mem.ReadInt32(addr + off);
            if (size is not int sz || sz <= 0 || sz > 63)
                continue;

            long? dataPtr = mem.ReadInt64(addr + off + 8);
            if (dataPtr is not long dp || !LooksLikeHeapPointer(dp))
                continue;

            string? text = mem.ReadShortAsciiString(dp, sz);
            if (text is { Length: > 0 })
                results.Add((off, sz, text));
        }
        return results;
    }

    /// <summary>Reads a CShipSetting's _itemName (the fuse currently "installed" in that Fuse Box socket, or "NULL").</summary>
    public static string? ReadShipSettingItemName(MemoryReader mem, long shipSettingAddr)
    {
        long fieldAddr = shipSettingAddr + GameOffsets.ShipSettingItemNameOffset;
        int? size = mem.ReadInt32(fieldAddr);
        long? dataPtr = mem.ReadInt64(fieldAddr + 8);
        if (size is not int sz || sz <= 0 || dataPtr is not long dp || !LooksLikeHeapPointer(dp))
            return null;

        return mem.ReadShortAsciiString(dp, Math.Min(sz, 63));
    }

    /// <summary>Reads a CShipSetting's _frameTarget - the name of the object whose displayed frame shows the
    /// installed fuse's icon (see GameOffsets.ShipSettingFrameTargetOffset).</summary>
    public static string? ReadShipSettingFrameTarget(MemoryReader mem, long shipSettingAddr)
    {
        long fieldAddr = shipSettingAddr + GameOffsets.ShipSettingFrameTargetOffset;
        int? size = mem.ReadInt32(fieldAddr);
        long? dataPtr = mem.ReadInt64(fieldAddr + 8);
        if (size is not int sz || sz <= 0 || dataPtr is not long dp || !LooksLikeHeapPointer(dp))
            return null;

        return mem.ReadShortAsciiString(dp, Math.Min(sz, 63));
    }

    /// <summary>Resolves the Fuse Box's own view [37,12,1] address (room 37 "Titania", node 12, view 1).</summary>
    public static long? FindFuseBoxView(MemoryReader mem, long project)
    {
        long? room = FindRoomByName(mem, project, RoomNames.GetName(37));
        if (room is null)
            return null;
        long? node = NthChildOfClass(mem, room.Value, "CNodeItem", 12);
        if (node is null)
            return null;
        return NthChildOfClass(mem, node.Value, "CViewItem", 1);
    }

    /// <summary>Finds every CShipSetting under the Fuse Box view [37,12,1] whose _itemName currently matches itemName -
    /// i.e. every socket a given fuse is (or is stuck being recorded as) installed in.</summary>
    public static List<long> FindShipSettingsInstalledWith(MemoryReader mem, long project, string itemName)
    {
        var results = new List<long>();

        long? view = FindFuseBoxView(mem, project);
        if (view is null)
            return results;

        foreach (long addr in FindAllDescendants(mem, view.Value, "CShipSetting"))
        {
            if (string.Equals(ReadShipSettingItemName(mem, addr), itemName, StringComparison.OrdinalIgnoreCase))
                results.Add(addr);
        }

        return results;
    }

    /// <summary>Bounded search of rootAddr's own subtree for the first node with an exact name match, regardless of class.</summary>
    public static long? FindDescendantByName(MemoryReader mem, long rootAddr, string name)
    {
        long? firstChild = mem.ReadInt64(rootAddr + GameOffsets.FirstChild);
        if (firstChild is null || !LooksLikeHeapPointer(firstChild.Value))
            return null;

        var visited = new HashSet<long>();
        var stack = new Stack<long>();
        stack.Push(firstChild.Value);
        int nodeCount = 0;

        while (stack.Count > 0 && nodeCount < MaxDescendantSearchNodes)
        {
            long addr = stack.Pop();
            if (addr == 0 || !visited.Add(addr))
                continue;
            nodeCount++;

            if (string.Equals(TryReadName(mem, addr), name, StringComparison.OrdinalIgnoreCase))
                return addr;

            long? childFirstChild = mem.ReadInt64(addr + GameOffsets.FirstChild);
            long? nextSibling = mem.ReadInt64(addr + GameOffsets.NextSibling);
            if (childFirstChild is not null && LooksLikeHeapPointer(childFirstChild.Value))
                stack.Push(childFirstChild.Value);
            if (nextSibling is not null && LooksLikeHeapPointer(nextSibling.Value))
                stack.Push(nextSibling.Value);
        }

        return null;
    }

    /// <summary>Finds the "Scraliontis Table" (CScraliontisTable) instance in the 1st Class Restaurant, live.</summary>
    public static long? FindScraliontisTable(MemoryReader mem, long project)
    {
        long? room = FindRoomByName(mem, project, "1stClassRestaurant");
        return room is null ? null : FindDescendant(mem, room.Value, "Scraliontis Table", "CScraliontisTable");
    }

    /// <summary>Resolves a full-state-machine item's home parent address, live.</summary>
    public static long? ResolveHomeParent(MemoryReader mem, long project, string itemName) =>
        ResolveHomeParent(mem, project, itemName, out _);

    /// <summary>Resolves a full-state-machine item's home parent address, live, reporting which step failed on a miss.</summary>
    public static long? ResolveHomeParent(MemoryReader mem, long project, string itemName, out string failureReason)
    {
        if (!ItemHomeLocations.TryGetHomeRnvs(itemName, out _))
        {
            failureReason = "no HomeRnvs entry";
            return null;
        }
        if (!ItemHomeLocations.TryGetDefaultParent(itemName, out DefaultParent defaultParent))
        {
            failureReason = "no DefaultParent entry";
            return null;
        }

        RoomNodeView[] rnvs = ItemHomeLocations.GetHomeParentSearchRnvs(itemName);
        var attempts = new List<string>();

        foreach (RoomNodeView rnv in rnvs)
        {
            long? room = FindRoomByName(mem, project, RoomNames.GetName(rnv.Room));
            if (room is null)
            {
                attempts.Add($"[{rnv.Room},{rnv.Node},{rnv.View}]: room '{RoomNames.GetName(rnv.Room)}' not found");
                continue;
            }

            long? node = NthChildOfClass(mem, room.Value, "CNodeItem", rnv.Node);
            if (node is null)
            {
                attempts.Add($"[{rnv.Room},{rnv.Node},{rnv.View}]: room found at 0x{room.Value:X} but CNodeItem #{rnv.Node} not found");
                continue;
            }

            long? view = NthChildOfClass(mem, node.Value, "CViewItem", rnv.View);
            if (view is null)
            {
                attempts.Add($"[{rnv.Room},{rnv.Node},{rnv.View}]: node found at 0x{node.Value:X} but CViewItem #{rnv.View} not found");
                continue;
            }

            if (defaultParent.ParentClass == "CViewItem")
            {
                failureReason = "";
                return view;
            }

            long? descendant = FindDescendant(mem, view.Value, defaultParent.ParentName, defaultParent.ParentClass);
            if (descendant is not null)
            {
                failureReason = "";
                return descendant;
            }

            attempts.Add($"[{rnv.Room},{rnv.Node},{rnv.View}]: view found at 0x{view.Value:X} but descendant '{defaultParent.ParentName ?? "(any name)"}' of class {defaultParent.ParentClass} not found");
        }

        failureReason = string.Join("; ", attempts);
        return null;
    }

    public readonly record struct MailItem(long Address, string Name, bool IsPendingMail, uint DestRoomFlags, uint RoomFlags);

    /// <summary>Reads every item currently parented under CMailMan, along with their mail-routing fields.</summary>
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
