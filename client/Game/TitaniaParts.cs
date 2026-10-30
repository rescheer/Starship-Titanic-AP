namespace StarshipTitanicAp;

/// <summary>One of the eleven parts that repair Titania.</summary>
/// <param name="Label">Short name for the UI.</param>
/// <param name="ApItemName">The AP item that says the part has been received.</param>
/// <param name="ItemName">The game's carry item (see <see cref="Items"/>).</param>
public readonly record struct TitaniaPart(string Label, string ApItemName, string ItemName);

public static class TitaniaParts
{
    public static readonly TitaniaPart[] All =
    {
        new("Nose", "Titania's Nose", "Nose"),
        new("Mouth", "Titania's Mouth", "Mouth"),
        new("Core", "Titania's Core", "CentralCore"),
        new("Eye (Elevator)", "Titania's Eye (Elevator)", "Eye2"),
        new("Eye (Light)", "Titania's Eye (Light)", "Eye1"),
        new("Ear (Bowl)", "Titania's Ear (Pistachio Bowl)", "Ear1"),
        new("Ear (Phonograph)", "Titania's Ear (Phonograph)", "Ear 2"),
        new("Olfactory Center", "Titania's Olfactory Center", "OlfactoryCentre"),
        new("Speech Center", "Titania's Speech Center", "SpeechCentre"),
        new("Vision Center", "Titania's Vision Center", "VisionCentre"),
        // A record the part's own check holds (items.py), not a delivery: the game hands over the part itself
        new("Auditory Center", "Titania's Auditory Center", "AuditoryCentre"),
    };
}
