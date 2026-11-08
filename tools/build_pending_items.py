"""Add an AP-owned pending-reward menu to private hub flow/message inputs."""
import argparse
from pathlib import Path


FLOW = '''

// Procedure39: append-only pending inventory menu; existing procedure indices stay fixed.
void ap_pending_items_menu()
{
    int action;
    CMM_CHECK_ENABLE_FUNC(900213);
    _pending_loop:
    CMM_CHECK_ENABLE_FUNC(900210);
    FLD_SMP_EVENT_BEGIN(0);
    SET_MSG_VAR(0, 0x4FC0, 5);
    SET_MSG_VAR(1, 0x4FC1, 5);
    SET_MSG_VAR(2, 0x4FC2, 5);
    SET_MSG_VAR(3, 0x4FC3, 5);
    SET_MSG_VAR(4, 0x4FC4, 5);
    SET_MSG_VAR(5, 0x4FC5, 5);
    SET_MSG_VAR(6, 0x4FC6, 5);
    MSG_WND_DSP();
    SEL_MASK(0);
    action = SEL_GENERIC(MSG_PENDING_ITEMS, PENDING_ITEMS_SEL);
    MSG_WND_CLS();
    FLD_SMP_EVENT_END(0);
    if (action < 0 || action == 9) { return; }
    if (action >= 0 && action < 6) { CMM_CHECK_ENABLE_FUNC(900220 + action); }
    else if (action == 6) { CMM_CHECK_ENABLE_FUNC(900214); }
    else if (action == 7) { CMM_CHECK_ENABLE_FUNC(900212); }
    else if (action == 8) { CMM_CHECK_ENABLE_FUNC(900211); }
    goto _pending_loop;
}
'''

MESSAGES = '''

[msg MSG_PENDING_ITEMS]
[s]Pending Items[n][var 6][e]

[sel PENDING_ITEMS_SEL top]
[s][var 0][e]
[s][var 1][e]
[s][var 2][e]
[s][var 3][e]
[s][var 4][e]
[s][var 5][e]
[s]Claim All[e]
[s]Previous Page[e]
[s]Next Page[e]
[s]Back[e]
'''


def transform(flow, messages):
    if FLOW in flow and MESSAGES in messages:
        return flow, messages
    start = flow.index("void ap_hotkey_menu()")
    end = flow.index("// Procedures below", start)
    root = flow[start:end]
    close = "    CMM_CHECK_ENABLE_FUNC(900135);"
    if root.count(close) != 1 or "ap_pending_items_menu" in flow:
        raise ValueError("Unexpected hub root; no source changed")
    root = root.replace(close,
        "    if (action == 3) { ap_pending_items_menu(); goto _ap_root; }\n" + close)
    menu = "[s]Warps[e]\n[s]Close[e]"
    if messages.count(menu) != 1 or "MSG_PENDING_ITEMS" in messages:
        raise ValueError("Unexpected hub selector; no source changed")
    return (flow[:start] + root + flow[end:] + FLOW,
            messages.replace(menu, "[s]Warps[e]\n[s]Pending Items[e]\n[s]Close[e]") + MESSAGES)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source_flow", type=Path)
    parser.add_argument("source_messages", type=Path)
    parser.add_argument("output_directory", type=Path)
    args = parser.parse_args()
    flow, messages = transform(args.source_flow.read_text(), args.source_messages.read_text())
    args.output_directory.mkdir(parents=True, exist_ok=True)
    (args.output_directory / args.source_flow.name).write_text(flow)
    (args.output_directory / args.source_messages.name).write_text(messages)


if __name__ == "__main__":
    main()
