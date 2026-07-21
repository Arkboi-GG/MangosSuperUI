## What is this?

MangosSuperUI Dual Spec gives your character **two talent specifications and swaps between them in place** — no respec cost, no loading screen, no relog. Your action bars come with you.

## How it works

- Open your **talent frame** (`N`). The Specialization pane is attached to its right edge.
- Each spec is a **card** showing its name, its point spread, and the artwork of its dominant tree. The one you are wearing is outlined in gold and badged **ACTIVE**.
- The first time you use the second slot, click its card. Your current build is stored, your talents are wiped for free, and the empty slot becomes yours to spend.
- Hit **Switch Specialization** at the bottom to swap. The arrow marks where you are heading.
- **Right-click** a card to rename it. Leave the name blank to go back to the automatic one.
- **Reset** clears a slot so you can rebuild it. On your active spec it also wipes your talents, again at no cost.

## What gets saved

Your talents and your action bars are captured **automatically, every time you switch away** from a spec. Spend points, rearrange your bars, then swap — that is the snapshot that comes back.

Spec names default to your dominant tree, so a build with 31 points in Protection labels itself Protection. Rename it and the custom name sticks.

## Requirements

- A GM-level account on the server (the addon uses `.spec` commands)
- Level 10 or above — the talent frame will not open below that

## Known limits

- **Items** on your bars only come back if the item is in your bags when you swap
- **Macros** are followed by name, so renaming a macro orphans its button
- Swapping is blocked in combat, while casting, and while dead

## Slash commands

- `/ds` — Switch to your other specialization
- `/ds switch` — Same as above
- `/ds save <n>` — Store your current build into slot `<n>`
- `/ds load <n>` — Switch to slot `<n>`
- `/ds name <n> <text>` — Rename a slot; blank text restores the automatic name
- `/ds forget <n>` — Clear one slot
- `/ds reset confirm` — Wipe every stored spec, name and bar layout for this character
- `/ds status` — List your slots and their point spreads
- `/ds debug` — Dump raw talent tab data (troubleshooting)

`/dualspec` works anywhere `/ds` does.
