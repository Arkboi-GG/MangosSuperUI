--[[ MSUI_LootBrowserData.lua

     GENERATED FILE. Produced by MangosSuperUI: /LootBrowser/Export
     Do not hand-edit -- regenerate after any lootifier or retexture run.

     FORMAT v3
     ---------
     MSUILB_DB.categories[n]     a macro category, collapsed in column 1
       .name                       "Dungeons" | "Raids" | "Crafting"
       .kind                       "dungeon" | "raid" | "crafting"
       .sets[n]                    ordered BY LEVEL, not alphabetically
         .name                     instance or profession name
         .level                    display label, e.g. "13-18" (optional)
         .nodes[n]                 shown in column 2
           .name                   boss name, "Trash Mobs", or a skill band
           .kind                   "boss" | "trash" | "profession"
           .entry                  creature entry, 0 for a folded trash node
           .items[n]               { itemId, chance, fallbackName, variants }

     variants[n] = { itemId, fallbackName, tierName }

     Every ordinary mob in an instance is folded into ONE "Trash Mobs" node.
     Listing forty individual mobs is what makes a loot browser unusable, and
     every loot browser worth using has made the same call.

     Variants come from vmangos_admin.lootifier_generated_items: a generated
     entry knows its base_entry and tier_name, so it is filed under the item it
     was minted from rather than listed as loot in its own right.

     chance is a percentage; 0 means no roll (crafted, or a vendor item).
     fallbackName only covers the second before the client's item query returns.

     The sample below is hand-made so the addon runs before the exporter does.
]]

MSUILB_DB = {
	version = 3,
	generated = "2026-07-20 (sample)",
	categories = {
		{
			name = "Dungeons",
			kind = "dungeon",
			sets = {
				{
					name = "Ragefire Chasm",
					level = "13-18",
					nodes = {
						{ name = "Taragaman the Hungerer", kind = "boss", entry = 11520, items = {
							{ 14145, 33.3, "Cursed Felblade" },
							{ 14148, 33.3, "Crystalline Cuffs" },
							{ 14149, 33.3, "Subterranean Cape" },
						} },
						{ name = "Jergosh the Invoker", kind = "boss", entry = 11518, items = {
							{ 14150, 33.3, "Robe of Evocation" },
							{ 14151, 33.3, "Chanting Blade" },
							{ 14147, 33.3, "Cavedweller Bracers" },
						} },
						{ name = "Trash Mobs", kind = "trash", entry = 0, items = {
							{ 2589, 25.0, "Linen Cloth" },
							{ 1179, 4.0, "Ice Cold Milk" },
						} },
					},
				},
				{
					name = "Wailing Caverns",
					level = "17-24",
					nodes = {
						{ name = "Deviate Faerie Dragon", kind = "boss", entry = 5912, items = {
							{ 6339, 20.0, "Feyscale Cloak", {
								{ 1100820, "Improved Feyscale Cloak", "Improved" },
								{ 1100825, "Feyscale Cloak of Power", "of Power" },
								{ 1100827, "Feyscale Cloak of Glory", "of Glory" },
								{ 1100829, "Deviate Faerie Dragon's Feyscale Cloak", "Legendary" },
							} },
							{ 6340, 18.0, "Firebelcher", {
								{ 1100815, "Firebelcher of Power", "of Power" },
								{ 1100819, "Deviate Faerie Dragon's Firebelcher", "Legendary" },
							} },
						} },
						{ name = "Trash Mobs", kind = "trash", entry = 0, items = {
							{ 5571, 8.0, "Small Lustrous Pearl" },
						} },
					},
				},
			},
		},
		{
			name = "Raids",
			kind = "raid",
			sets = {
				{
					name = "Molten Core",
					level = "60",
					nodes = {
						{ name = "Lucifron", kind = "boss", entry = 12118, items = {
							{ 16800, 20.0, "Arcanist Boots" },
						} },
						{ name = "Trash Mobs", kind = "trash", entry = 0, items = {
							{ 17011, 12.0, "Lava Core" },
						} },
					},
				},
			},
		},
		{
			name = "Crafting",
			kind = "crafting",
			sets = {
				{
					name = "Leatherworking",
					nodes = {
						{ name = "Leatherworking", kind = "profession", entry = 165, items = {
							{ 2308, 0, "Fine Leather Boots", {
								{ 1079158, "Improved Fine Leather Boots", "Improved" },
								{ 1079162, "Fine Leather Boots of Power", "of Power" },
							} },
							{ 2311, 0, "Dark Leather Tunic" },
						} },
					},
				},
			},
		},
	},
};
