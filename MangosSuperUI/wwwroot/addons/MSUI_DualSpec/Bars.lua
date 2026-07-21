-- MangosSuperUI_DualSpec :: Bars.lua   (v0.3)
-- Client-side action bar capture and restore.
--
-- WHY THIS EXISTS: the 1.12 client discards SMSG_ACTION_BUTTONS outside the
-- world-enter sequence, so the server cannot repaint the bars mid-session.
-- Placing the buttons locally is the only way to swap bars without a loading
-- screen. Every PlaceAction here emits a normal CMSG_SET_ACTION_BUTTON, so
-- character_action ends up correct on the server for free.
--
-- 1.12 has no GetActionInfo. Capture is therefore:
--     GetActionText(slot)      -> non-nil means MACRO (returns the macro name)
--     tooltip scan             -> spell/item name + rank
--     GetActionTexture(slot)   -> icon, used as a cheap "already correct" check
-- Restore resolves a name+rank back to a live spellbook index at swap time,
-- because indices shift as talents are learned and unlearned.

local D = MSUI_DualSpec

D.MAX_SLOTS = 120
D.PER_TICK = 4              -- placements per frame; keeps the packet burst sane
D.RESTORE_DELAY = 1.75      -- seconds after a swap before placing
D.USE_SERVER_BARS = nil     -- set to 1 to also fire ".spec bars <n>" (redundant now)

-- ============================================================
-- Hidden tooltip used for scanning action slots
-- ============================================================

local scan = CreateFrame("GameTooltip", "MSUIDualSpecScan", nil, "GameTooltipTemplate")
scan:SetOwner(UIParent, "ANCHOR_NONE")

local function ScanSlot(slot)
    scan:SetOwner(UIParent, "ANCHOR_NONE")
    scan:ClearLines()
    scan:SetAction(slot)
    local left = getglobal("MSUIDualSpecScanTextLeft1")
    local right = getglobal("MSUIDualSpecScanTextRight1")
    local name, rank
    if left then name = left:GetText() end
    if right then rank = right:GetText() end
    scan:Hide()
    if name == "" then name = nil end
    if rank == "" then rank = nil end
    return name, rank
end

-- ============================================================
-- Capture
-- ============================================================

function D.CaptureBars()
    local bars = {}
    local slot = 1
    while slot <= D.MAX_SLOTS do
        if HasAction(slot) then
            local entry = {}
            entry.texture = GetActionTexture(slot)

            local macroName = GetActionText(slot)
            if macroName then
                entry.kind = "macro"
                entry.name = macroName
            else
                -- spell or item; we do not try to tell them apart here.
                -- Restore tries the spellbook first, then the bags.
                entry.kind = "auto"
                local name, rank = ScanSlot(slot)
                entry.name = name
                entry.rank = rank
            end

            if entry.name or entry.texture then
                bars[slot] = entry
            end
        end
        slot = slot + 1
    end
    return bars
end

function D.CountBars(bars)
    if not bars then return 0 end
    local n, slot = 0, 1
    while slot <= D.MAX_SLOTS do
        if bars[slot] then n = n + 1 end
        slot = slot + 1
    end
    return n
end

-- ============================================================
-- Resolution
-- ============================================================

-- Build name -> spellbook index. Keys:
--     "Name|Rank 3"  exact rank
--     "#Name"        highest known rank (last index wins)
--     "Name"         first (lowest) rank
local function BuildSpellIndex()
    local map = {}
    local i = 1
    while i <= 1024 do
        local name, rank = GetSpellName(i, BOOKTYPE_SPELL)
        if not name then break end
        if rank and rank ~= "" then
            local key = name .. "|" .. rank
            if not map[key] then map[key] = i end
        end
        if not map[name] then map[name] = i end
        map["#" .. name] = i
        i = i + 1
    end
    return map
end

local function ResolveSpell(map, want)
    if not map or not want.name then return nil end
    if want.rank then
        local exact = map[want.name .. "|" .. want.rank]
        if exact then return exact end
    end
    local highest = map["#" .. want.name]
    if highest then return highest end
    return map[want.name]
end

local function FindItemInBags(name)
    if not name then return nil end
    local bag = 0
    while bag <= 4 do
        local slots = GetContainerNumSlots(bag)
        if slots and slots > 0 then
            local s = 1
            while s <= slots do
                local link = GetContainerItemLink(bag, s)
                if link then
                    local _, _, itemName = string.find(link, "%[(.+)%]")
                    if itemName == name then
                        return bag, s
                    end
                end
                s = s + 1
            end
        end
        bag = bag + 1
    end
    return nil
end

-- ============================================================
-- Restore pump
-- ============================================================

local pump = CreateFrame("Frame", "MSUIDualSpecBarPump")
pump:Hide()

local function AlreadyCorrect(slot, want)
    if not HasAction(slot) then return nil end
    if want.kind == "macro" then
        return GetActionText(slot) == want.name
    end
    if want.texture and GetActionTexture(slot) == want.texture then
        -- texture matches; ranks share icons, so confirm by name when we can
        if not want.name then return 1 end
        local name, rank = ScanSlot(slot)
        if name == want.name and rank == want.rank then return 1 end
        return nil
    end
    return nil
end

local function ProcessSlot(slot, want)
    if not want then
        if HasAction(slot) then
            PickupAction(slot)
            ClearCursor()
        end
        return
    end

    if AlreadyCorrect(slot, want) then
        D._skipped = D._skipped + 1
        return
    end

    ClearCursor()

    if want.kind == "macro" then
        local idx = GetMacroIndexByName(want.name)
        if idx and idx > 0 then
            PickupMacro(idx)
        end
    else
        local idx = ResolveSpell(D._spellIndex, want)
        if idx then
            PickupSpell(idx, BOOKTYPE_SPELL)
        else
            local bag, bslot = FindItemInBags(want.name)
            if bag then
                PickupContainerItem(bag, bslot)
            end
        end
    end

    -- Verify by result rather than by cursor API - CursorHasMacro does not
    -- exist in 1.12 and PlaceAction is a no-op on an empty cursor.
    PlaceAction(slot)
    ClearCursor()

    if HasAction(slot) then
        D._placed = D._placed + 1
    else
        D._failed = D._failed + 1
        if want.name then
            table.insert(D._missing, want.name)
        end
    end
end

pump:SetScript("OnUpdate", function()
    local n = 0
    while n < D.PER_TICK and D._at <= D.MAX_SLOTS do
        ProcessSlot(D._at, D._want[D._at])
        D._at = D._at + 1
        n = n + 1
    end

    if D._at > D.MAX_SLOTS then
        pump:Hide()
        D._spellIndex = nil
        D._want = nil

        local msg = "Bars: " .. D._placed .. " placed"
        if D._skipped > 0 then msg = msg .. ", " .. D._skipped .. " already right" end
        if D._failed > 0 then msg = msg .. ", |cffff8080" .. D._failed .. " unresolved|r" end
        D.Print(msg)

        local miss, i = "", 1
        while D._missing[i] and i <= 6 do
            if i > 1 then miss = miss .. ", " end
            miss = miss .. D._missing[i]
            i = i + 1
        end
        if miss ~= "" then
            D.Error("Could not place: " .. miss)
        end

        if D.RefreshUI then D.RefreshUI() end
    end
end)

function D.RestoreBars(spec)
    local db = D.DB()
    local s = db.specs[spec]
    if not s or not s.bars then
        D.Print("No stored bar layout for this spec yet - it will be captured on the next swap.")
        return
    end
    if pump:IsVisible() then return end

    D._want = s.bars
    D._spellIndex = BuildSpellIndex()
    D._at = 1
    D._placed = 0
    D._failed = 0
    D._skipped = 0
    D._missing = {}
    pump:Show()
end

-- ============================================================
-- Wire into the swap
-- ============================================================

function D.OnSwapComplete(n)
    D.After(D.RESTORE_DELAY, function()
        if D.USE_SERVER_BARS then
            D.GM("spec bars " .. string.format("%d", n))
        end
        D.RestoreBars(n)
    end)
end
