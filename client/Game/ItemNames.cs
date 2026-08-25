namespace StarshipTitanicAp;

/// <summary>
/// Canonical list of the 40 carryable item names. This is a fixed part of
/// the game's own data (g_vm->_itemIds, loaded once at startup from the
/// TEXT/ITEM_IDS resource - see TitanicEngine::setItemNames() and
/// Debugger::cmdItem() in the ScummVM Titanic engine source, which
/// iterates exactly 40 entries and is how CCarry-derived items are
/// resolved by name: findByName(itemName)).
///
/// Obtained via the ScummVM debug console's "item" command (no
/// arguments) run in-game, which prints this exact list - no memory
/// reading needed to get these; they never change at runtime.
/// </summary>
public static class ItemNames
{
    public static readonly string[] All =
    {
        "MaitreD Left Arm",
        "MaitreD Right Arm",
        "OlfactoryCentre",
        "AuditoryCentre",
        "SpeechCentre",
        "VisionCentre",
        "CentralCore",
        "Perch",
        "SeasonBridge",
        "FanBridge",
        "BeamBridge",
        "ChickenBridge",
        "CarryParrot",
        "Chicken",
        "CrushedTV",
        "Feathers",
        "Lemon",
        "BeerGlass",
        "BigHammer",
        "Ear1",
        "Ear 2",
        "Eye1",
        "Eye2",
        "Mouth",
        "Nose",
        "NoseSpare",
        "Hose",
        "DeadHoseSpare",
        "HoseEnd",
        "DeadHoseEndSpare",
        "BrokenLiftbotHead",
        "LongStick",
        "Magazine",
        "Napkin",
        "Phonograph Cylinder",
        "Phonograph Cylinder 1",
        "Phonograph Cylinder 2",
        "Phonograph Cylinder 3",
        "Photograph",
        "Music System Key",
    };

    private static readonly HashSet<string> Set = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>True if the given tree-node name is one of the 40 known carryable items.</summary>
    public static bool IsKnownItemName(string name) => Set.Contains(name);
}
