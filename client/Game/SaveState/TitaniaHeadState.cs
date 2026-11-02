namespace StarshipTitanicAp;

/// <summary>A slot on Titania's face (CHeadSlot), the part that always goes in it, and the brain card that makes that
/// sense work once it's in.</summary>
internal readonly record struct TitaniaHeadSlot(string Name, string Part, string Card);

/// <summary>A slot in Titania's head that takes any brain card (CBrainSlot), and the card that's right for it, with the
/// frame CBrainSlot::AddHeadPieceMsg shows that card with and the line its _pieceAdded is on.</summary>
internal readonly record struct TitaniaBrainSlot(string Name, string Card, int CardFrame, int CardPieceAddedField);

/// <summary>What's installed in Titania: each face slot and the core, which only ever hold their own part, and each brain
/// slot's card, which can be the wrong one. Packed into one integer for slot data storage. A found part may not be
/// installed, and all but the ears can be taken out until she wakes, so checks can't show this. CTitania's flags and the
/// slots' sense states follow from it.</summary>
internal sealed record TitaniaHeadState(bool[] Face, bool Core, string?[] Brain)
{
    /// <summary>The data storage key this is kept under.</summary>
    public const string StorageKey = "titania_head";

    public const string CoreSlotName = "CentralCoreSlot";
    public const string CorePart = "CentralCore";

    /// <summary>In the order of <see cref="Face"/>.</summary>
    public static readonly TitaniaHeadSlot[] FaceSlots =
    {
        new("Eye1Slot", "Eye1", "VisionCentre"),
        new("Eye2Slot", "Eye2", "VisionCentre"),
        new("Ear1Slot", "Ear1", "AuditoryCentre"),
        new("Ear2Slot", "Ear 2", "AuditoryCentre"),
        new("MouthSlot", "Mouth", "SpeechCentre"),
        new("NoseSlot", "Nose", "OlfactoryCentre"),
    };

    /// <summary>In the order of <see cref="Brain"/>. A card is encoded with its position in this list plus one, and 0 for
    /// an empty slot, so only append to it.</summary>
    public static readonly TitaniaBrainSlot[] BrainSlots =
    {
        new("OlfactoryCentreSlot", "OlfactoryCentre", 2, 2),
        new("AuditoryCentreSlot", "AuditoryCentre", 1, 3),
        new("SpeechCentreSlot", "SpeechCentre", 3, 6),
        new("VisionCentreSlot", "VisionCentre", 4, 3),
    };

    private const int CoreBit = 6;
    private const int BrainShift = CoreBit + 1;
    private const int BrainBits = 3;
    private const int BrainMask = (1 << BrainBits) - 1;
    private static readonly int TotalBits = BrainShift + BrainSlots.Length * BrainBits;

    /// <summary>A new game's Titania.</summary>
    public static readonly TitaniaHeadState Initial = new(new bool[FaceSlots.Length], false, new string?[BrainSlots.Length]);

    /// <summary>Titania with every part where it belongs.</summary>
    public static readonly TitaniaHeadState Repaired = new(
        FaceSlots.Select(_ => true).ToArray(),
        true,
        BrainSlots.Select(slot => (string?)slot.Card).ToArray());

    /// <summary>The parts in any slot, right or wrong.</summary>
    public IEnumerable<string> InstalledParts()
    {
        for (int i = 0; i < FaceSlots.Length; i++)
        {
            if (Face[i])
                yield return FaceSlots[i].Part;
        }
        if (Core)
            yield return CorePart;
        foreach (string? card in Brain)
        {
            if (card is not null)
                yield return card;
        }
    }

    /// <summary>What CTitania's flag for a part holds: a card only counts in its own slot.</summary>
    public bool IsPartWorking(string part)
    {
        if (part == CorePart)
            return Core;
        int cardSlot = Array.FindIndex(BrainSlots, slot => slot.Card == part);
        return cardSlot >= 0
            ? Brain[cardSlot] == part
            : Face[Array.FindIndex(FaceSlots, slot => slot.Part == part)];
    }

    /// <summary>The value CTitania's "CheckHead" sends a face slot.</summary>
    public string SenseFor(TitaniaHeadSlot faceSlot)
    {
        if (!FaceSlots.Where(slot => slot.Card == faceSlot.Card).All(slot => IsPartWorking(slot.Part)))
            return "Not Working";
        return IsPartWorking(faceSlot.Card) ? "Working" : "Random";
    }

    public long Encode()
    {
        long packed = Core ? 1L << CoreBit : 0;
        for (int i = 0; i < FaceSlots.Length; i++)
        {
            if (Face[i])
                packed |= 1L << i;
        }
        for (int i = 0; i < BrainSlots.Length; i++)
        {
            int card = Array.FindIndex(BrainSlots, slot => slot.Card == Brain[i]) + 1;
            packed |= (long)card << (BrainShift + i * BrainBits);
        }
        return packed;
    }

    /// <summary>Null for no value, or one this client can't make sense of.</summary>
    public static TitaniaHeadState? Decode(long? value)
    {
        if (value is not { } packed || packed < 0 || packed >> TotalBits != 0)
            return null;

        var face = new bool[FaceSlots.Length];
        for (int i = 0; i < face.Length; i++)
            face[i] = (packed >> i & 1) != 0;

        var brain = new string?[BrainSlots.Length];
        for (int i = 0; i < brain.Length; i++)
        {
            int card = (int)(packed >> (BrainShift + i * BrainBits)) & BrainMask;
            if (card > BrainSlots.Length)
                return null;
            brain[i] = card == 0 ? null : BrainSlots[card - 1].Card;
        }
        return new TitaniaHeadState(face, (packed >> CoreBit & 1) != 0, brain);
    }
}
