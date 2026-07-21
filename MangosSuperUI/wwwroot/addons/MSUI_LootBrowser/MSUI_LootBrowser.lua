--[[ MSUI LootBrowser -- an in-game loot browser for MangosSuperUI.

     The three-column shape is a familiar one; the data underneath is not. It is
     exported from the server this addon was downloaded from, so nothing here is
     curated by hand and nothing is a snapshot of retail vanilla.

     Data lives in MSUI_LootBrowserData.lua and is GENERATED from the server's databases
     (see /LootBrowser/Export in MangosSuperUI), so lootified and retextured items
     are in it by construction.

     v3 layout, three columns:

       COLUMN 1  macro categories -- Dungeons, Raids, Crafting -- collapsed by
                 default. Clicking one lists what is inside it, ordered by level
                 rather than alphabetically: Ragefire Chasm, then The Deadmines,
                 then Wailing Caverns, and so on.
       COLUMN 2  the selected instance's bosses, with every ordinary mob folded
                 into a single "Trash Mobs" node. Listing 40 individual mobs is
                 what makes a loot browser unusable.
       COLUMN 3  the loot, with lootified variants nested under the base item
                 they were minted from.

     Vanilla 1.12 / Lua 5.0 rules throughout:
       SetPoint always takes 5 arguments
       handlers read this / event / arg1, never a self parameter
       no string.match, no string.gmatch, no # operator, no table.getn
]]

MSUILB_Settings = MSUILB_Settings or {};

local CAT_ROWS  = 19;
local CAT_H     = 20;
local NAV_ROWS  = 19;
local NAV_H     = 20;
local LIST_ROWS = 11;
local LIST_H    = 34;
local FALLBACK_ICON = "Interface\\Icons\\INV_Misc_QuestionMark";

local catRows  = {};
local navRows  = {};
local listRows = {};

local catFlat    = {};    -- column 1 rows: categories + their sets
local catCount   = 0;
local navFlat    = {};    -- column 2 rows: nodes of the selected set
local navCount   = 0;
local view       = {};    -- column 3 rows: items + expanded variants
local viewCount  = 0;

local selectedSet  = nil;
local selectedNode = nil;
local expanded   = {};
local filterText = "";
local pending    = 0;
local scanner    = nil;
local searchBox  = nil;

-- Tier colours. The lootifier wrote two naming conventions: lowercase names
-- (power / glory / gods) came from the drop lootifier and are always creature
-- tied; capitalised ones (Improved / of Glory / Legendary / of Azeroth ...)
-- are mostly base tied, which is the crafting side.
local TIER_COLOR = {
	["improved"]    = { r = 0.30, g = 0.90, b = 0.30 },
	["power"]       = { r = 0.30, g = 0.60, b = 1.00 },
	["of power"]    = { r = 0.30, g = 0.60, b = 1.00 },
	["glory"]       = { r = 0.75, g = 0.40, b = 1.00 },
	["of glory"]    = { r = 0.75, g = 0.40, b = 1.00 },
	["of fury"]     = { r = 1.00, g = 0.50, b = 0.20 },
	["gods"]        = { r = 1.00, g = 0.82, b = 0.00 },
	["of the gods"] = { r = 1.00, g = 0.82, b = 0.00 },
	["legendary"]   = { r = 1.00, g = 0.50, b = 0.00 },
	["of azeroth"]  = { r = 0.90, g = 0.30, b = 0.30 },
	["immortal"]    = { r = 1.00, g = 1.00, b = 1.00 },
};

local function Print(msg)
	DEFAULT_CHAT_FRAME:AddMessage("|cff44ccff[LootBrowser]|r " .. tostring(msg));
end

local function TierColor(tier)
	if ( tier ) then
		local c = TIER_COLOR[string.lower(tier)];
		if ( c ) then return c; end
	end
	return { r = 0.7, g = 0.7, b = 0.7 };
end

-- table.getn is avoided on purpose; every count is walked.
local function Count(t)
	if ( not t ) then return 0; end
	local n = 0;
	while ( t[n + 1] ) do n = n + 1; end
	return n;
end

-- ---------------------------------------------------------------- item cache
--
-- GetItemInfo returns nil for anything the client has never seen -- which is
-- every custom item the first time it is shown. Asking a tooltip for the link
-- makes the client query the server; the row refills on the next tick.

local function ItemLink(id)
	return "item:" .. id .. ":0:0:0";
end

local function WarmItem(id)
	if ( not scanner ) then
		scanner = CreateFrame("GameTooltip", "MSUILB_Scanner", nil, "GameTooltipTemplate");
		scanner:SetOwner(WorldFrame, "ANCHOR_NONE");
	end
	scanner:ClearLines();
	scanner:SetHyperlink(ItemLink(id));
end

local QUALITY_FALLBACK = { r = 1, g = 1, b = 1 };

local function QualityColor(quality)
	if ( quality and ITEM_QUALITY_COLORS and ITEM_QUALITY_COLORS[quality] ) then
		return ITEM_QUALITY_COLORS[quality];
	end
	return QUALITY_FALLBACK;
end

-- ---------------------------------------------------------------- data shim
--
-- v3 data is categories -> sets -> nodes. A v1/v2 file is sets -> nodes, so it
-- is wrapped in a single category and still browses.

local function Categories()
	if ( not MSUILB_DB ) then return {}; end
	if ( MSUILB_DB.categories ) then return MSUILB_DB.categories; end
	if ( MSUILB_DB.sets ) then
		if ( not MSUILB_DB._shim ) then
			MSUILB_DB._shim = { { name = "Loot", kind = "legacy", sets = MSUILB_DB.sets } };
		end
		return MSUILB_DB._shim;
	end
	return {};
end

-- ---------------------------------------------------------------- column 1

local function BuildCatFlat()
	catFlat = {};
	catCount = 0;

	local cats = Categories();
	local ci = 1;
	while ( cats[ci] ) do
		local cat = cats[ci];
		catCount = catCount + 1;
		catFlat[catCount] = { kind = "cat", label = cat.name, cat = cat };

		if ( cat.expanded ) then
			local si = 1;
			while ( cat.sets and cat.sets[si] ) do
				local set = cat.sets[si];
				local label = set.name;
				if ( set.level ) then label = label .. "  |cff808080" .. set.level .. "|r"; end
				catCount = catCount + 1;
				catFlat[catCount] = { kind = "set", label = label, cat = cat, set = set };
				si = si + 1;
			end
		end
		ci = ci + 1;
	end
end

-- ---------------------------------------------------------------- column 2

local function BuildNavFlat()
	navFlat = {};
	navCount = 0;
	if ( not selectedSet or not selectedSet.nodes ) then return; end

	local i = 1;
	while ( selectedSet.nodes[i] ) do
		navCount = navCount + 1;
		navFlat[navCount] = selectedSet.nodes[i];
		i = i + 1;
	end
end

-- ---------------------------------------------------------------- column 3

local function Matches(text)
	if ( filterText == "" ) then return true; end
	if ( not text ) then return false; end
	return string.find(string.lower(text), filterText, 1, true) ~= nil;
end

local function BuildView()
	view = {};
	viewCount = 0;
	if ( not selectedNode or not selectedNode.items ) then return; end

	local i = 1;
	while ( selectedNode.items[i] ) do
		local rec      = selectedNode.items[i];
		local id       = rec[1];
		local chance   = rec[2];
		local name     = rec[3];
		local variants = rec[4];
		local vCount   = Count(variants);

		-- a filter hit on any variant has to pull its base into view too
		local childHit = false;
		if ( filterText ~= "" and vCount > 0 ) then
			local k = 1;
			while ( variants[k] ) do
				if ( Matches(variants[k][2]) ) then childHit = true; end
				k = k + 1;
			end
		end

		local selfHit = Matches(name);

		if ( selfHit or childHit ) then
			-- If the base itself never drops here, the only real numbers in the
			-- group belong to the variants. Sum them so the collapsed row still
			-- says how often SOMETHING from this family drops.
			local vTotal = 0;
			if ( vCount > 0 ) then
				local t = 1;
				while ( variants[t] ) do
					if ( variants[t][4] ) then vTotal = vTotal + variants[t][4]; end
					t = t + 1;
				end
			end

			viewCount = viewCount + 1;
			view[viewCount] = { kind = "item", id = id, chance = chance,
			                    name = name, quality = rec[5],
			                    vCount = vCount, vTotal = vTotal };

			if ( expanded[id] or childHit ) then
				local k = 1;
				while ( variants and variants[k] ) do
					if ( filterText == "" or selfHit or Matches(variants[k][2]) ) then
						viewCount = viewCount + 1;
						view[viewCount] = { kind = "variant", id = variants[k][1],
						                    name = variants[k][2], tier = variants[k][3],
						                    chance = variants[k][4], quality = variants[k][5] };
					end
					k = k + 1;
				end
			end
		end
		i = i + 1;
	end
end

-- ---------------------------------------------------------------- rendering

function MSUILB_CatUpdate()
	local offset = FauxScrollFrame_GetOffset(MSUILB_CatScroll) or 0;
	FauxScrollFrame_Update(MSUILB_CatScroll, catCount, CAT_ROWS, CAT_H);

	local i = 1;
	while ( i <= CAT_ROWS ) do
		local row = catRows[i];
		local entry = catFlat[offset + i];
		if ( entry ) then
			row.entry = entry;
			row.text:ClearAllPoints();
			if ( entry.kind == "cat" ) then
				local mark = "+";
				if ( entry.cat.expanded ) then mark = "-"; end
				row.text:SetText("[" .. mark .. "] " .. entry.label);
				row.text:SetTextColor(1, 0.82, 0);
				row.text:SetPoint("LEFT", row, "LEFT", 4, 0);
			else
				row.text:SetText(entry.label);
				row.text:SetTextColor(0.85, 0.85, 0.85);
				row.text:SetPoint("LEFT", row, "LEFT", 16, 0);
			end
			if ( entry.set and entry.set == selectedSet ) then
				row.highlight:Show();
			else
				row.highlight:Hide();
			end
			row:Show();
		else
			row.entry = nil;
			row:Hide();
		end
		i = i + 1;
	end
end

function MSUILB_NavUpdate()
	local offset = FauxScrollFrame_GetOffset(MSUILB_NavScroll) or 0;
	FauxScrollFrame_Update(MSUILB_NavScroll, navCount, NAV_ROWS, NAV_H);

	local i = 1;
	while ( i <= NAV_ROWS ) do
		local row = navRows[i];
		local node = navFlat[offset + i];
		if ( node ) then
			row.node = node;
			row.text:SetText(node.name);
			if ( node.kind == "trash" ) then
				row.text:SetTextColor(0.65, 0.65, 0.65);
			else
				row.text:SetTextColor(0.95, 0.95, 0.95);
			end
			if ( node == selectedNode ) then
				row.highlight:Show();
			else
				row.highlight:Hide();
			end
			row:Show();
		else
			row.node = nil;
			row:Hide();
		end
		i = i + 1;
	end
end

function MSUILB_ListUpdate()
	local offset = FauxScrollFrame_GetOffset(MSUILB_ListScroll) or 0;
	FauxScrollFrame_Update(MSUILB_ListScroll, viewCount, LIST_ROWS, LIST_H);

	pending = 0;

	local i = 1;
	while ( i <= LIST_ROWS ) do
		local row = listRows[i];
		local rec = view[offset + i];

		if ( rec ) then
			row.itemId = rec.id;
			row.isVariant = (rec.kind == "variant");

			local name, link, quality, _, _, _, _, _, texture = GetItemInfo(rec.id);

			-- The client caches item data by entry in itemcache.wdb. Generated
			-- entries get reused between lootifier runs, so that cache can hand
			-- back a name the server no longer uses. The exported name came out
			-- of item_template, so for custom entries it wins; GetItemInfo is
			-- still used for the icon and the quality colour.
			if ( rec.id >= 1000000 and rec.name and rec.name ~= "" ) then
				name = rec.name;
			end

			-- Quality has the same problem as the name: the cached copy can be a
			-- tier or two off after entries are reused, which is why a legendary
			-- was rendering blue. item_template.Quality is the truth.
			if ( rec.quality ) then
				quality = rec.quality;
			end

			row.icon:ClearAllPoints();
			row.name:ClearAllPoints();
			if ( row.isVariant ) then
				row.icon:SetWidth(20);
				row.icon:SetHeight(20);
				row.icon:SetPoint("LEFT", row, "LEFT", 24, 0);
			else
				row.icon:SetWidth(28);
				row.icon:SetHeight(28);
				row.icon:SetPoint("LEFT", row, "LEFT", 2, 0);
			end
			row.name:SetPoint("LEFT", row.icon, "RIGHT", 8, 0);

			if ( not name ) then
				pending = pending + 1;
				WarmItem(rec.id);
				row.name:SetText(rec.name or ("item " .. rec.id));
				if ( rec.quality ) then
					local c = QualityColor(rec.quality);
					row.name:SetTextColor(c.r, c.g, c.b);
				else
					row.name:SetTextColor(0.55, 0.55, 0.55);
				end
				row.icon:SetTexture(FALLBACK_ICON);
			else
				local c = QualityColor(quality);
				row.name:SetText(name);
				row.name:SetTextColor(c.r, c.g, c.b);
				row.icon:SetTexture(texture or FALLBACK_ICON);
			end

			if ( row.isVariant ) then
				local tc = TierColor(rec.tier);
				if ( rec.chance and rec.chance > 0 ) then
					-- no tier badge: the item's own name already says "Improved"
					-- or "of Glory", so repeating it only crowded the row
					row.chance:SetText(string.format("%.1f%%", rec.chance));
					row.chance:SetTextColor(0.8, 0.8, 0.8);
				else
					row.chance:SetText(rec.tier or "variant");
					row.chance:SetTextColor(tc.r, tc.g, tc.b);
				end
			elseif ( rec.chance and rec.chance > 0 ) then
				-- The lootifier splits an item's drop chance across its variants,
				-- so the original is often left with a sliver. Show that sliver as
				-- the row's own number, with the variant total dimmed beside it.
				if ( rec.vTotal and rec.vTotal > 0 ) then
					row.chance:SetText(string.format("%.1f%%|cff707070 +%.1f%%|r",
						rec.chance, rec.vTotal));
				else
					row.chance:SetText(string.format("%.1f%%", rec.chance));
				end
				row.chance:SetTextColor(0.8, 0.8, 0.8);
			elseif ( rec.vTotal and rec.vTotal > 0 ) then
				-- the original does not drop here at all; only the variants do
				row.chance:SetText(string.format("|cff707070+%.1f%%|r", rec.vTotal));
				row.chance:SetTextColor(0.55, 0.55, 0.55);
			else
				row.chance:SetText("");
			end

			if ( row.isVariant ) then
				row.badge:Hide();
			elseif ( rec.vCount and rec.vCount > 0 ) then
				if ( expanded[rec.id] ) then
					row.badge:SetText("[-] " .. rec.vCount);
				else
					row.badge:SetText("[+] " .. rec.vCount);
				end
				row.badge:Show();
			else
				row.badge:Hide();
			end

			row:Show();
		else
			row.itemId = nil;
			row:Hide();
		end
		i = i + 1;
	end

	if ( selectedNode ) then
		local extra = "";
		if ( pending > 0 ) then extra = "  (" .. pending .. " loading from the server)"; end
		if ( filterText ~= "" ) then extra = extra .. "  filtered"; end
		local where = "";
		if ( selectedSet ) then where = selectedSet.name .. " -- "; end
		MSUILB_Status:SetText(where .. selectedNode.name .. " -- " .. viewCount .. " rows" .. extra);
	elseif ( selectedSet ) then
		MSUILB_Status:SetText(selectedSet.name .. " -- pick a boss.");
	else
		MSUILB_Status:SetText("Pick a category on the left.");
	end
end

function MSUILB_Refresh()
	BuildCatFlat();
	BuildNavFlat();
	BuildView();
	MSUILB_CatUpdate();
	MSUILB_NavUpdate();
	MSUILB_ListUpdate();
end

local function ResetScroll(scroll, bar)
	if ( FauxScrollFrame_SetOffset ) then FauxScrollFrame_SetOffset(scroll, 0); end
	if ( bar ) then bar:SetValue(0); end
end

-- ---------------------------------------------------------------- row events

local function CatRow_OnClick()
	local entry = this.entry;
	if ( not entry ) then return; end

	if ( entry.kind == "cat" ) then
		entry.cat.expanded = not entry.cat.expanded;
		MSUILB_Refresh();
		return;
	end

	selectedSet = entry.set;
	selectedNode = nil;
	expanded = {};
	ResetScroll(MSUILB_NavScroll, MSUILB_NavScrollScrollBar);
	ResetScroll(MSUILB_ListScroll, MSUILB_ListScrollScrollBar);
	MSUILB_Refresh();
end

local function NavRow_OnClick()
	if ( not this.node ) then return; end
	selectedNode = this.node;
	expanded = {};
	ResetScroll(MSUILB_ListScroll, MSUILB_ListScrollScrollBar);
	MSUILB_Refresh();
end

local function ListRow_OnEnter()
	if ( not this.itemId ) then return; end
	GameTooltip:SetOwner(this, "ANCHOR_RIGHT");
	GameTooltip:SetHyperlink(ItemLink(this.itemId));
	GameTooltip:Show();
end

local function ListRow_OnLeave()
	GameTooltip:Hide();
end

local function ListRow_OnClick()
	if ( not this.itemId ) then return; end
	local name, link = GetItemInfo(this.itemId);

	if ( IsShiftKeyDown() ) then
		if ( link ) then
			if ( ChatFrameEditBox and ChatFrameEditBox:IsVisible() ) then
				ChatFrameEditBox:Insert(link);
			else
				ChatEdit_InsertLink(link);
			end
		end
		return;
	end

	if ( IsControlKeyDown() ) then
		if ( link ) then DressUpItemLink(link); end
		return;
	end

	if ( expanded[this.itemId] ) then
		expanded[this.itemId] = nil;
	else
		expanded[this.itemId] = true;
	end
	BuildView();
	MSUILB_ListUpdate();
end

-- ---------------------------------------------------------------- row build

local function MakeTextRow(name, parent, prev, width, height, onClick, first)
	local row = CreateFrame("Button", name, parent);
	row:SetWidth(width);
	row:SetHeight(height);
	if ( first ) then
		row:SetPoint("TOPLEFT", parent, "TOPLEFT", 0, 0);
	else
		row:SetPoint("TOPLEFT", prev, "BOTTOMLEFT", 0, 0);
	end

	local hl = row:CreateTexture(nil, "BACKGROUND");
	hl:SetTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight");
	hl:SetBlendMode("ADD");
	hl:SetAllPoints(row);
	hl:Hide();
	row.highlight = hl;

	row:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight", "ADD");

	local fs = row:CreateFontString(nil, "ARTWORK", "GameFontNormalSmall");
	fs:SetPoint("LEFT", row, "LEFT", 4, 0);
	fs:SetJustifyH("LEFT");
	fs:SetWidth(width - 8);
	row.text = fs;

	row:SetScript("OnClick", onClick);
	return row;
end

local function BuildCatRows()
	local i = 1;
	while ( i <= CAT_ROWS ) do
		catRows[i] = MakeTextRow("MSUILB_CatRow" .. i, MSUILB_Cat,
			catRows[i - 1], 166, CAT_H, CatRow_OnClick, i == 1);
		i = i + 1;
	end
end

local function BuildNavRows()
	local i = 1;
	while ( i <= NAV_ROWS ) do
		navRows[i] = MakeTextRow("MSUILB_NavRow" .. i, MSUILB_Nav,
			navRows[i - 1], 166, NAV_H, NavRow_OnClick, i == 1);
		i = i + 1;
	end
end

local function BuildListRows()
	local i = 1;
	while ( i <= LIST_ROWS ) do
		local row = CreateFrame("Button", "MSUILB_ListRow" .. i, MSUILB_List);
		row:SetWidth(390);
		row:SetHeight(LIST_H);
		if ( i == 1 ) then
			row:SetPoint("TOPLEFT", MSUILB_List, "TOPLEFT", 0, 0);
		else
			row:SetPoint("TOPLEFT", listRows[i - 1], "BOTTOMLEFT", 0, 0);
		end
		row:RegisterForClicks("LeftButtonUp", "RightButtonUp");
		row:SetHighlightTexture("Interface\\QuestFrame\\UI-QuestTitleHighlight", "ADD");

		local icon = row:CreateTexture(nil, "ARTWORK");
		icon:SetWidth(28);
		icon:SetHeight(28);
		icon:SetPoint("LEFT", row, "LEFT", 2, 0);
		row.icon = icon;

		local name = row:CreateFontString(nil, "ARTWORK", "GameFontNormal");
		name:SetPoint("LEFT", icon, "RIGHT", 8, 0);
		name:SetJustifyH("LEFT");
		name:SetWidth(228);
		row.name = name;

		local badge = row:CreateFontString(nil, "ARTWORK", "GameFontDisableSmall");
		badge:SetPoint("RIGHT", row, "RIGHT", -110, 0);
		badge:SetJustifyH("RIGHT");
		row.badge = badge;

		local chance = row:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall");
		chance:SetPoint("RIGHT", row, "RIGHT", -6, 0);
		chance:SetJustifyH("RIGHT");
		chance:SetWidth(104);
		row.chance = chance;

		row:SetScript("OnEnter", ListRow_OnEnter);
		row:SetScript("OnLeave", ListRow_OnLeave);
		row:SetScript("OnClick", ListRow_OnClick);
		listRows[i] = row;
		i = i + 1;
	end
end

-- The filter box is built in Lua rather than in the XML so that a missing
-- template is a recoverable error instead of a parse failure that would take
-- the whole addon down.
local function BuildSearch()
	local ok = pcall(function()
		searchBox = CreateFrame("EditBox", "MSUILB_Search", MSUILB_Frame, "InputBoxTemplate");
	end);
	if ( not ok or not searchBox ) then
		searchBox = nil;
		return;
	end
	searchBox:SetWidth(190);
	searchBox:SetHeight(20);
	searchBox:SetPoint("BOTTOMLEFT", MSUILB_List, "TOPLEFT", 8, 6);
	searchBox:SetAutoFocus(false);
	searchBox:SetScript("OnTextChanged", function()
		filterText = string.lower(this:GetText() or "");
		BuildView();
		MSUILB_ListUpdate();
	end);
	searchBox:SetScript("OnEscapePressed", function()
		this:SetText("");
		this:ClearFocus();
	end);
end

-- ---------------------------------------------------------------- minimap
--
-- The usual vanilla minimap button: a 31x31 Button parented to Minimap, with the
-- tracking-border ring over a trimmed icon. Position is an ANGLE in degrees, not
-- a point, so the button stays on the ring wherever the minimap ends up and the
-- saved value survives a UI scale change.

local minimapBtn = nil;
local MINIMAP_RADIUS = 80;

function MSUILB_UpdateMinimapPosition()
	if ( not minimapBtn ) then return; end
	local angle = math.rad(MSUILB_Settings.minimapPos or 205);
	minimapBtn:ClearAllPoints();
	minimapBtn:SetPoint("CENTER", Minimap, "CENTER",
		math.cos(angle) * MINIMAP_RADIUS, math.sin(angle) * MINIMAP_RADIUS);
end

-- While dragging, the angle is read back off the cursor. GetCursorPosition is in
-- screen pixels, so it has to be divided by the effective scale before it can be
-- compared against a frame's own coordinates.
local function MinimapButton_OnUpdate()
	local mx, my = Minimap:GetCenter();
	if ( not mx ) then return; end
	local scale = Minimap:GetEffectiveScale();
	local px, py = GetCursorPosition();
	px = px / scale;
	py = py / scale;
	MSUILB_Settings.minimapPos = math.deg(math.atan2(py - my, px - mx));
	MSUILB_UpdateMinimapPosition();
end

local function MinimapButton_OnEnter()
	GameTooltip:SetOwner(this, "ANCHOR_LEFT");
	GameTooltip:AddLine("MSUI LootBrowser");
	GameTooltip:AddLine("Left-click to open.", 0.9, 0.9, 0.9);
	GameTooltip:AddLine("Drag to move around the minimap.", 0.6, 0.6, 0.6);
	if ( MSUILB_DB and MSUILB_DB.generated ) then
		GameTooltip:AddLine("Data: " .. MSUILB_DB.generated, 0.5, 0.5, 0.5);
	end
	GameTooltip:Show();
end

local function BuildMinimapButton()
	if ( not Minimap ) then return; end

	local btn = CreateFrame("Button", "MSUILB_MinimapButton", Minimap);
	btn:SetWidth(31);
	btn:SetHeight(31);
	btn:SetFrameStrata("MEDIUM");
	btn:RegisterForClicks("LeftButtonUp", "RightButtonUp");
	btn:RegisterForDrag("LeftButton");

	-- Swap this path to change the icon; nothing else depends on it.
	local icon = btn:CreateTexture(nil, "BACKGROUND");
	icon:SetTexture("Interface\\Icons\\INV_Misc_Book_09");
	icon:SetWidth(20);
	icon:SetHeight(20);
	icon:SetPoint("CENTER", btn, "CENTER", 0, 1);
	-- trims the square icon border so it sits inside the round ring
	icon:SetTexCoord(0.075, 0.925, 0.075, 0.925);

	local border = btn:CreateTexture(nil, "OVERLAY");
	border:SetTexture("Interface\\Minimap\\MiniMap-TrackingBorder");
	border:SetWidth(53);
	border:SetHeight(53);
	border:SetPoint("TOPLEFT", btn, "TOPLEFT", 0, 0);

	btn:SetHighlightTexture("Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight", "ADD");

	btn:SetScript("OnClick", function()
		MSUILB_Toggle();
	end);
	btn:SetScript("OnEnter", MinimapButton_OnEnter);
	btn:SetScript("OnLeave", function()
		GameTooltip:Hide();
	end);
	btn:SetScript("OnDragStart", function()
		this:SetScript("OnUpdate", MinimapButton_OnUpdate);
	end);
	btn:SetScript("OnDragStop", function()
		this:SetScript("OnUpdate", nil);
	end);

	minimapBtn = btn;
	MSUILB_UpdateMinimapPosition();

	if ( MSUILB_Settings.minimapHide ) then
		btn:Hide();
	end
end

function MSUILB_ToggleMinimapButton()
	if ( not minimapBtn ) then return; end
	if ( MSUILB_Settings.minimapHide ) then
		MSUILB_Settings.minimapHide = nil;
		minimapBtn:Show();
		Print("minimap button shown.");
	else
		MSUILB_Settings.minimapHide = 1;
		minimapBtn:Hide();
		Print("minimap button hidden. /lb minimap to bring it back.");
	end
end

-- ---------------------------------------------------------------- lifecycle

function MSUILB_SavePosition()
	local point, _, relPoint, x, y = MSUILB_Frame:GetPoint();
	MSUILB_Settings.point = point;
	MSUILB_Settings.relPoint = relPoint;
	MSUILB_Settings.x = x;
	MSUILB_Settings.y = y;
end

local function RestorePosition()
	if ( not MSUILB_Settings.point ) then return; end
	MSUILB_Frame:ClearAllPoints();
	MSUILB_Frame:SetPoint(MSUILB_Settings.point, UIParent,
		MSUILB_Settings.relPoint, MSUILB_Settings.x, MSUILB_Settings.y);
end

function MSUILB_OnLoad()
	this:RegisterForDrag("LeftButton");

	-- WHITE8X8 tinted black. The list is dense enough that a transparent
	-- background makes rows hard to read against the world behind it.
	if ( this.SetBackdropColor ) then
		this:SetBackdropColor(0, 0, 0, 0.94);
		this:SetBackdropBorderColor(1, 1, 1, 0.9);
	end
	BuildCatRows();
	BuildNavRows();
	BuildListRows();
	BuildSearch();
	BuildMinimapButton();

	-- open the first category so the window is never blank on first use
	local cats = Categories();
	if ( cats[1] ) then cats[1].expanded = true; end

	local title = "MSUI LootBrowser";
	if ( MSUILB_DB and MSUILB_DB.generated ) then
		title = "MSUI LootBrowser  -  data generated " .. MSUILB_DB.generated;
	end
	MSUILB_Title:SetText(title);
end

function MSUILB_Toggle()
	if ( MSUILB_Frame:IsVisible() ) then
		MSUILB_Frame:Hide();
	else
		RestorePosition();
		MSUILB_Frame:Show();
	end
end

-- Rows waiting on the server are refilled about a second later.
local ticker = CreateFrame("Frame");
ticker.elapsed = 0;
ticker:SetScript("OnUpdate", function()
	if ( not MSUILB_Frame or not MSUILB_Frame:IsVisible() or pending == 0 ) then return; end
	this.elapsed = this.elapsed + arg1;
	if ( this.elapsed < 1.0 ) then return; end
	this.elapsed = 0;
	MSUILB_ListUpdate();
end);

SLASH_MSUILB1 = "/lootbrowser";
SLASH_MSUILB2 = "/lb";
SlashCmdList["MSUILB"] = function(msg)
	msg = string.lower(msg or "");
	if ( string.find(msg, "minimap", 1, true) ) then
		MSUILB_ToggleMinimapButton();
		return;
	end
	MSUILB_Toggle();
end;
