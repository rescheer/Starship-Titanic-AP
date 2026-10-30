namespace StarshipTitanicAp;

/// <summary>What has to be true, besides a room's Access Card, to enter it (rules.py's entrance rules).</summary>
internal enum RoomRequirement { None, SecondClass, FirstClass, TitaniaRepaired }

/// <summary>A room on the main window's deck plan.</summary>
/// <param name="Name">The tile's name.</param>
/// <param name="Card">The room's Access Card item, for the rooms that have one.</param>
/// <param name="LocationPrefixes">The location-name prefixes ("&lt;prefix&gt; - ...") that belong to the tile. Follows
/// locations.py's regions: the Bomb Room's Succ-U-Bus is in Titania's Room, and the Broken Elevator is boarded from the
/// 2nd Class Lobby.</param>
internal sealed record RoomDefinition(string Name, string? Card, RoomRequirement Requirement, params string[] LocationPrefixes);

/// <summary>One row of the deck plan.</summary>
internal sealed record DeckDefinition(string Label, RoomDefinition[] Rooms);

internal static class RoomTiles
{
    public static readonly DeckDefinition[] Decks =
    {
        new("BRIDGE", new RoomDefinition[]
        {
            new("Bridge", "Bridge Access Card", RoomRequirement.TitaniaRepaired, "Bridge"),
        }),
        new("1ST", new RoomDefinition[]
        {
            new("Arboretum", "Arboretum Access Card", RoomRequirement.FirstClass, "Arboretum"),
            new("Restaurant", "1st Class Restaurant Access Card", RoomRequirement.FirstClass, "1st Class Restaurant"),
            new("1st Staterooms", "1st Class Staterooms Access Card", RoomRequirement.FirstClass, "1st Class Lobby", "1st Class Stateroom"),
        }),
        new("2ND", new RoomDefinition[]
        {
            new("Creator's Chamber", "Creator's Chamber Access Card", RoomRequirement.SecondClass, "Creator's Chamber"),
            new("Sculpture", "Sculpture Chamber Access Card", RoomRequirement.SecondClass, "Sculpture Chamber"),
            new("Promenade Deck", "Promenade Deck Access Card", RoomRequirement.SecondClass, "Promenade Deck"),
            new("Bar", "Bar Access Card", RoomRequirement.SecondClass, "Bar"),
            new("Music Room", "Music Room Access Card", RoomRequirement.SecondClass, "Music Room"),
            new("2nd Staterooms", "2nd Class Staterooms Access Card", RoomRequirement.SecondClass, "2nd Class Lobby", "2nd Class Stateroom", "Broken Elevator"),
        }),
        new("SGT", new RoomDefinition[]
        {
            new("Top of the Well", "Top of the Well Access Card", RoomRequirement.None, "Top of the Well"),
            new("Parrot Lobby", "Parrot Lobby Access Card", RoomRequirement.None, "Parrot Lobby"),
            new("Titania's Room", "Titania's Room Access Card", RoomRequirement.None, "Titania's Room", "Bomb Room"),
            new("SGT Staterooms", "SGT Class Staterooms Access Card", RoomRequirement.None, "SGT Class Lobby", "SGT Class Stateroom"),
        }),
        new("LOWER", new RoomDefinition[]
        {
            new("Embarkation", null, RoomRequirement.None, "Embarkation Lobby", "DeskBot"),
            new("Bottom of Well", "Bottom of the Well Access Card", RoomRequirement.None, "Bottom of the Well"),
            new("Bilge Room", "Bilge Room Access Card", RoomRequirement.None, "Bilge Room"),
        }),
    };
}
