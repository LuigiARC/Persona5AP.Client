#!/usr/bin/env python3
"""Generate our native selector UI; no extracted or decompiled game source is used."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HUB = ROOT.parent / "private-backups/persona-5-ap-local/src/P5RArchipelago/Hub/FSCR8000_002_000"
MARKER = "// BEGIN GENERATED CONNECTION MENUS"

FLOW = '''// BEGIN GENERATED CONNECTION MENUS
// Procedure35: native connection menu. SET_MSG_VAR type5 uses the item-name getter;
// managed reserved names supply status/fields without changing any game inventory.
void ap_connection_menu()
{
    int action;
    CMM_CHECK_ENABLE_FUNC(900180);
    _connection_loop:
    FLD_SMP_EVENT_BEGIN(0);
    SET_MSG_VAR(0, 0x4FE0, 5);
    SET_MSG_VAR(1, 0x4FE1, 5);
    SET_MSG_VAR(2, 0x4FE2, 5);
    SET_MSG_VAR(3, 0x4FE3, 5);
    SET_MSG_VAR(4, 0x4FE5, 5);
    MSG_WND_DSP();
    SEL_MASK(0);
    action = SEL_GENERIC(MSG_CONNECTION_STATUS, CONNECTION_SEL);
    MSG_WND_CLS();
    FLD_SMP_EVENT_END(0);
    if (action == 0) { ap_connection_edit(1); }
    else if (action == 1) { ap_connection_edit(2); }
    else if (action == 2) { ap_connection_edit(3); }
    else if (action == 3) { CMM_CHECK_ENABLE_FUNC(900182); }
    else if (action == 4) { CMM_CHECK_ENABLE_FUNC(900183); }
    else if (action == 5) { CMM_CHECK_ENABLE_FUNC(900185); }
    else if (action == 7 || action < 0) { CMM_CHECK_ENABLE_FUNC(900181); return; }
    goto _connection_loop;
}

// Procedure36: draft editing never touches Joker/team names; cancel discards the draft.
void ap_connection_edit(int field)
{
    int action;
    CMM_CHECK_ENABLE_FUNC(900190 + field);
    _editor_loop:
    FLD_SMP_EVENT_BEGIN(0);
    if (CMM_CHECK_ENABLE_FUNC(960000 + MSG_CONNECTION_KEYBOARD) == 0)
    {
        FLD_SMP_EVENT_END(0);
        MSG_WND_DSP(); MSG(MSG_CONNECTION_KEYBOARD_UNAVAILABLE, 0); MSG_WND_CLS();
        return;
    }
    // Real field opcodes stop existing movement as well as blocking new input.
    // Managed900199 restores their original values, rather than blindly unlocking.
    FLD_SET_PAD_LOCK(1);
    FLD_SET_MOVE_LOCK(1);
    SET_MSG_VAR(0, 0x4FE4, 5);
    MSG_WND_DSP();
    SEL_MASK(0);
    action = SEL_GENERIC(MSG_CONNECTION_KEYBOARD, CONNECTION_KEYBOARD_SEL);
    MSG_WND_CLS();
    CMM_CHECK_ENABLE_FUNC(900199);
    FLD_SMP_EVENT_END(0);
    if (action == 0)
    {
        if (CMM_CHECK_ENABLE_FUNC(900196) == 1) { return; }
        FLD_SMP_EVENT_BEGIN(0);
        MSG_WND_DSP(); MSG(MSG_CONNECTION_INVALID, 0); MSG_WND_CLS();
        FLD_SMP_EVENT_END(0);
    }
    else { CMM_CHECK_ENABLE_FUNC(900197); return; }
    goto _editor_loop;
}

// Procedure37: deprecated character picker. Keep its index so Warps remains38.
void ap_connection_chars(int page)
{
    return;
}
'''

MESSAGES = '''// BEGIN GENERATED CONNECTION MENUS
[msg MSG_CONNECTION_STATUS]
[s]Archipelago Connection[e]

[sel CONNECTION_SEL top]
[s]Host: [var 1][e]
[s]Slot: [var 2][e]
[s]Password: [var 3][e]
[s]Connect / Reconnect[e]
[s]Disconnect[e]
[s]Auto-connect: [var 4][e]
[s]Status: [var 0] (Refresh)[e]
[s]Back[e]

[msg MSG_CONNECTION_INVALID]
[s]Invalid host/port or slot, or connection change still in progress.[n]Slot names must contain 1 to 16 characters.[n][w][e]

[msg MSG_CONNECTION_KEYBOARD_UNAVAILABLE]
[s]The native keyboard editor is unavailable in this field.[n]Check the Reloaded log for an unsupported input signature.[n][w][e]

[msg MSG_CONNECTION_KEYBOARD]
[s]Edit: [var 0][e]

[sel CONNECTION_KEYBOARD_SEL top]
[s]Enter - Save[e]
[s]Escape - Cancel[e]

[msg MSG_AP_WARPS]
[s]Archipelago Warps[e]

[sel AP_WARPS_SEL top]
[s]Palaces[e]
[s]Mementos Paths / Depths[e]
[s]Return to Mementos Hub[e]
[s]Back[e]
'''

def main():
    flow, messages = FLOW, MESSAGES
    flow += '''
// Procedure38: existing verified warp paths, still gated by received AP access/card items.
void ap_warps_menu()
{
    int action;
    CMM_CHECK_ENABLE_FUNC(900189);
    FLD_SMP_EVENT_BEGIN(0);
    MSG_WND_DSP();
    SEL_MASK(0); action = SEL_GENERIC(MSG_AP_WARPS, AP_WARPS_SEL); MSG_WND_CLS();
    FLD_SMP_EVENT_END(0);
    if (action == 0) { CMM_CHECK_ENABLE_FUNC(900187); apply_ap_party_recruits(); apply_ap_party_levels(); palace_menu(); }
    else if (action == 1) { CMM_CHECK_ENABLE_FUNC(900187); mementos_checkpoint_menu(); }
    else if (action == 2)
    {
        CMM_CHECK_ENABLE_FUNC(900187);
        BIT_OFF((0x20000000 + 80)); deactivate_ap_palaces();
        BIT_ON((0x20000000 + 200)); CMM_CHECK_ENABLE_FUNC(900136);
        CMM_CHECK_ENABLE_FUNC(900135);
        FADEOUT(0, 10); FADE_SYNC(); CALL_FIELD(190, 1, 1, 0);
    }
}
'''
    for suffix, generated in ((".flow", flow), (".msg", messages)):
        path = HUB.with_suffix(suffix)
        old = path.read_text().split(MARKER, 1)[0].rstrip()
        path.write_text(old + "\n\n" + generated)

if __name__ == "__main__":
    main()
