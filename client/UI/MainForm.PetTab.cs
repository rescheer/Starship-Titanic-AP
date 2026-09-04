namespace StarshipTitanicAp;

public sealed partial class MainForm
{
    // --- PET tab (was Actions) ---
    private readonly ComboBox _cmbClass = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Button _btnSetClass = new() { Text = "Set Class", Width = 100 };
    private readonly TextBox _txtMsgText = new() { Width = 300, PlaceholderText = "free text message" };
    private readonly Button _btnDisplayMessageText = new() { Text = "Display Free Text", Width = 180 };

    private TabPage BuildPetTab()
    {
        var page = new TabPage("PET");
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(10),
            AutoScroll = true,
        };

        _cmbClass.Items.AddRange(new object[] { "1 - First Class", "2 - Second Class", "3 - Third Class", "4 - No Class" });
        _cmbClass.SelectedIndex = 0;

        layout.Controls.Add(SectionLabel("Passenger Class"));
        var classRow = new FlowLayoutPanel { AutoSize = true };
        classRow.Controls.Add(_cmbClass);
        classRow.Controls.Add(_btnSetClass);
        layout.Controls.Add(classRow);
        layout.Controls.Add(HelpLabel("Gates room access immediately and updates the PET color right away (writes class, then calls reset() + markAllDirty())."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Free Text Message"));
        layout.Controls.Add(_txtMsgText);
        layout.Controls.Add(_btnDisplayMessageText);
        layout.Controls.Add(HelpLabel("Uses GameActions.DisplayMessageSmart: logs the message via the real CPetConversations::displayMessage(const CString&), and if the Conversation tab isn't the one currently visible, also shows it immediately via the older CPetControl::displayMessage(const CString&, int) (DisplayPetMessageText) so it isn't missed. Needs only the PET control address, available from early in a session."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("PET Talk Command Hook [experimental]"));
        var hookRow = new FlowLayoutPanel { AutoSize = true };
        hookRow.Controls.Add(_btnInstallHook);
        hookRow.Controls.Add(_btnUninstallHook);
        layout.Controls.Add(hookRow);
        layout.Controls.Add(_lblHookStatus);
        layout.Controls.Add(HelpLabel("Intercepts textLineEntered(). Lines starting with '!' are captured here and blocked from reaching TrueTalk; anything else behaves normally. Installs automatically on attach. Captured commands appear in the feedback line at the top of the window. Verify the stub in x64dbg before relying on this - genuinely experimental compared to the rest of this app."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Class Upgrade Lock [experimental]"));
        var classLockRow = new FlowLayoutPanel { AutoSize = true };
        classLockRow.Controls.Add(_btnInstallClassLockHook);
        classLockRow.Controls.Add(_btnUninstallClassLockHook);
        layout.Controls.Add(classLockRow);
        layout.Controls.Add(_lblClassLockHookStatus);
        layout.Controls.Add(_chkAllowInitialUpgrade);
        layout.Controls.Add(HelpLabel("Blocks CGameObject::setPassengerClass() so the DeskBot can't change PassengerClass on its own, and reports the attempted class as its matching location check ('DeskBot - Second/First Class Upgrade') - the actual upgrade still only ever applies from receiving the matching item over the multiworld. The very first upgrade (None -> Third, needed just to leave the Embarkation Lobby) has no corresponding AP location/item yet, so it's handled separately: when checked, this box applies that one upgrade directly ourselves rather than leaving the player stuck; when unchecked, it's blocked and logged the same as any other unrecognized attempt, for whenever the apworld grows a real item for it. Installs automatically on attach. Try a legitimate DeskBot upgrade with the lock installed and confirm PassengerClass on the Live tab doesn't move, and that the attempt shows up in the feedback line."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Maitre'D Table Lock [experimental]"));
        var maitreDLockRow = new FlowLayoutPanel { AutoSize = true };
        maitreDLockRow.Controls.Add(_btnInstallMaitreDHook);
        maitreDLockRow.Controls.Add(_btnUninstallMaitreDHook);
        layout.Controls.Add(maitreDLockRow);
        layout.Controls.Add(_lblMaitreDHookStatus);
        layout.Controls.Add(HelpLabel("Blocks CScraliontisTable::MaitreDDefeatedMsg() so defeating the MaitreD reports 'Defeated MaitreD' as its location check instead of unlocking the table directly - table access still only ever applies from receiving the 'Table Access' item over the multiworld (GameActions.GrantScraliontisTableAccess). This avoids a softlock: in vanilla, the Music System Key only becomes available after winning this fight, but AP can grant it earlier, and completing the Music Room puzzle first permanently blocks further interaction with Maitre'D. Installs automatically on attach. Try defeating the MaitreD with the lock installed and confirm you still can't sit down until 'Table Access' is granted."));

        layout.Controls.Add(Spacer());
        layout.Controls.Add(SectionLabel("Broken Elevator Eye Gate [experimental]"));
        var liftEyeGateRow = new FlowLayoutPanel { AutoSize = true };
        liftEyeGateRow.Controls.Add(_btnInstallGetLiftEye2GateHook);
        liftEyeGateRow.Controls.Add(_btnUninstallGetLiftEye2GateHook);
        layout.Controls.Add(liftEyeGateRow);
        layout.Controls.Add(_lblGetLiftEye2GateHookStatus);
        layout.Controls.Add(HelpLabel("Prevents a real softlock: the broken elevator's Titania's Eye pickup (CGetLiftEye2::MouseDragStartMsg) never checks the underlying Eye item's own _canTake at all - it forwards a CPassOnDragStartMsg straight to it - so this app injects an extra condition into that function's own \"checkPoint succeeded\" branch: the pickup only proceeds once the LiftBot Head is both AP-granted and physically in the player's inventory, otherwise it's blocked exactly like a failed checkPoint() (same as the vanilla function's own miss case - no message from the game itself), and this app shows its own message explaining why. A plain RNV arrival reminder can't do this job since the elevator's (Room, Node, View) is shared by every lift in the game. Installs automatically on attach. Try dragging the Eye without the Head in hand and confirm the pickup is blocked and the warning appears."));

        page.Controls.Add(layout);
        return page;
    }

    private void DoSetClass()
    {
        if (!RequireAttachedAndResolved(out long gameManager))
            return;

        int newClass = _cmbClass.SelectedIndex + 1;

        if (_currentInventoryRoom is null)
        {
            bool wroteOnly = GameActions.SetPassengerClass(_mem, gameManager, newClass);
            ShowActionResult(wroteOnly, $"Set class to {newClass} (CPetControl not resolved yet - color won't refresh immediately)");
            return;
        }

        bool ok = GameActions.SetPassengerClassFull(_mem, gameManager, _currentInventoryRoom.Value, newClass);
        ShowActionResult(ok, $"Set class to {newClass}");
    }

    private void DoDisplayMessageText()
    {
        if (!_mem.IsAttached)
        {
            ShowActionResult(false, "Not attached");
            return;
        }
        if (string.IsNullOrEmpty(_txtMsgText.Text))
        {
            ShowActionResult(false, "Enter a message first");
            return;
        }
        if (_currentInventoryRoom is null)
        {
            ShowActionResult(false, "PET control address not resolved yet");
            return;
        }

        bool ok = GameActions.DisplayMessageSmart(_mem, _currentInventoryRoom.Value, _txtMsgText.Text);
        ShowActionResult(ok, $"Displayed message: \"{_txtMsgText.Text}\"");
    }
}
