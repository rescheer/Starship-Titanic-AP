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
        // Never naturally visible until the Phonograph puzzle is solved, so the room's baked background art
        // does NOT already show it - the generic restoration flow forcing _visible false left it present but
        // unclickable (no pickup cursor on hover). No captured bounds/cursorId override yet beyond KeepVisible;
        // if that alone isn't enough, capture live values via ItemFieldsForm and fill them in here.
        RestoreFieldOverride = new RestoreFieldOverride(78, 266, 254, 386, 8, -1, KeepVisible: true),
        // This item's engine-side pickup (CPhonographEar::PETGainedObjectMsg) spawns a "Replacement Phonograph
        // Ear" (CReplacementEar) into the same view a second later, as set-dressing so the Phonograph socket
        // doesn't look empty. AP's mail/inventory delivery still triggers that same pickup message, so the
        // replacement spawns even though the player never actually took the real ear - and once we then restore
        // this item back to its home parent for a real pickup, the replacement is left sitting visible on top of
        // it, blocking the click. CReplacementEar has no other persisted state (see phonograph_ear.cpp/
        // replacement_ear.cpp in ScummVM's Titanic engine), so forcing its _visible flag false is the complete fix.
        HideSiblingOnRestore = ("Replacement Phonograph Ear", "CReplacementEar"),
        // Stays un-pickable in the mail until the Phonograph puzzle is completed unless we force it.
        RequiresCanTakeOverride = true,
    };
}
