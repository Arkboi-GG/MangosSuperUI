An in-game loot browser generated from your server's own database.

The three-column shape will look familiar to anyone who has used a loot browser before. The data underneath is not: nothing in here is curated by hand and nothing is a snapshot of retail vanilla. It is exported directly from the world database, so the drop chances are the ones the server actually rolls, and any custom or generated items appear on their own because they are already in the loot tables the export reads.

## Using it

- Click the minimap button, or type `/lootbrowser` or `/lb`. Drag the window anywhere; it remembers where you put it.
- The minimap button drags around the ring to wherever you want it, and stays there. `/lb minimap` hides it, and the same command brings it back.
- The left column holds the macro categories: **Dungeons**, **Raids**, **Crafting**. Click one to open it. Dungeons and raids are ordered by level rather than alphabetically, so the list reads the way you would progress.
- The middle column lists that instance's bosses, with every ordinary mob folded into a single **Trash Mobs** entry. A list of forty individual mobs is not a loot browser.
- The right column is the loot. Hover any row for the real in-game tooltip.

## Reading a loot row

- **Shift-click** puts the item link in your chat box.
- **Ctrl-click** opens it in the dressing room.
- A **`[+] 4`** badge means the item has four variants generated from it. Click the row to expand them underneath, indented, each with its own drop chance and tier. Click again to collapse.
- A collapsed row showing a dimmed percentage is one where the base item does not drop there at all. That number is the combined chance of its variants, so the row still tells you how often something from that family drops.
- The search box above the loot list filters the current boss, variants included. Searching a tier name shows every base item that has a variant in that tier, with just that variant under it.

## Custom items are blank for a second

The client has never seen a server-generated item, so it asks the server the first time a row is drawn. The status line reports how many rows are waiting. They fill in about a second later with the real name, icon and quality colour. Nothing is broken — it is the same request the game makes when someone links an item you have not seen before.

## Keeping it current

The data file is a snapshot, so it goes stale when items are generated or loot tables change.

The Downloads page compares the packaged data against the live database and reports when it has drifted and by how much. When it flags, click **Regenerate data**, then **Download**, replace the addon folder, and `/reload` in game.

## Files

- `MSUI_LootBrowser.toc` — load order. Lua before XML, because the frame's `OnLoad` runs while the XML is still being parsed.
- `MSUI_LootBrowser.xml` — the window: three columns and their scroll frames.
- `MSUI_LootBrowser.lua` — everything else.
- `MSUI_LootBrowserData.lua` — the generated data. The only file that changes between downloads.

The window position, the minimap button position and whether it is hidden are all kept in `MSUILB_Settings`, so they survive a re-download.
