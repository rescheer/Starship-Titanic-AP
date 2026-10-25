namespace StarshipTitanicAp;

internal static class Ear2Item
{
    public static readonly ItemDefinition Definition = new()
    {
        Name = "Ear 2",
        ApItemName = "Titania's Ear (Phonograph)",
        PickupLocationName = "Music Room - Titania's Ear (Phonograph)",
        HomeRnvs = new[] { new RoomNodeView(12, 2, 1) },
        DefaultParent = new DefaultParent(null, "CViewItem"),
        RestoreFieldOverride = new RestoreFieldOverride(78, 266, 254, 386, 8, -1, KeepVisible: true),
        // Spawned by CPhonographEar::PETGainedObjectMsg as set-dressing for the empty Phonograph socket.
        HideSiblingOnRestore = ("Replacement Phonograph Ear", "CReplacementEar"),
        RequiresCanTakeOverride = true,
    };
}
