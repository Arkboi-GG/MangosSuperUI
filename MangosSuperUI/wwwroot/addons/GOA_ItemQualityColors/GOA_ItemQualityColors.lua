-- GOA renumbered item qualities server-side to insert Reforged (5) and Relic (8)
-- between Epic/Legendary and after Artifact: Legendary moved 5->6, Artifact 6->7.
-- The stock client's ITEM_QUALITY_COLORS table (set in UIParent.lua) still has the
-- OLD mapping (5=orange/Legendary, 6=light-gold/Artifact, nothing past 6), so every
-- entry from 5 up must be corrected here to match what the server now sends.
if ITEM_QUALITY_COLORS then
    ITEM_QUALITY_COLORS[5] = { r = 0.251, g = 0.769, b = 1.000 } -- Reforged (cyan)
    ITEM_QUALITY_COLORS[6] = { r = 1.000, g = 0.502, b = 0.000 } -- Legendary (orange, was index 5)
    ITEM_QUALITY_COLORS[7] = { r = 0.902, g = 0.800, b = 0.502 } -- Artifact (light gold, was index 6)
    ITEM_QUALITY_COLORS[8] = { r = 0.902, g = 0.125, b = 0.125 } -- Relic (red)
end
