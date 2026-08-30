-- GOA renumbered item qualities server-side to insert Reforged (5) and Relic (8)
-- between Epic/Legendary and after Artifact: Legendary moved 5->6, Artifact 6->7.
--
-- ITEM_QUALITY_COLORS (UIParent.lua:65) is NOT a real color table -- it's a cache
-- populated once at load by calling the native GetItemQualityColor(i) for i=-1..6.
-- Anything that reads live quality colors (GameTooltip, chat item links, etc.)
-- calls GetItemQualityColor(quality) directly, not the cache table -- so the
-- cache alone can't be patched, the native function itself has to be wrapped.
local GOA_OrigGetItemQualityColor = GetItemQualityColor
local GOA_COLORS = {
    [5] = { 0.251, 0.769, 1.000, "ff40c4ff" }, -- Reforged (cyan)
    [6] = { 1.000, 0.502, 0.000, "ffff8000" }, -- Legendary (orange, was index 5)
    [7] = { 0.902, 0.800, 0.502, "ffe6cc80" }, -- Artifact (light gold, was index 6)
    [8] = { 0.902, 0.125, 0.125, "ffe62020" }, -- Relic (red)
}

function GetItemQualityColor(quality)
    local c = GOA_COLORS[quality]
    if c then
        return c[1], c[2], c[3], c[4]
    end
    return GOA_OrigGetItemQualityColor(quality)
end

-- Refresh the cache table too, for any code reading ITEM_QUALITY_COLORS directly
-- (e.g. MSUI_LootBrowser) instead of calling the function live.
if ITEM_QUALITY_COLORS then
    for quality in pairs(GOA_COLORS) do
        local r, g, b, hex = GetItemQualityColor(quality)
        ITEM_QUALITY_COLORS[quality] = { r = r, g = g, b = b, hex = hex }
    end
end
