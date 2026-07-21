-- MangosSuperUI_DualSpec :: Core.lua   (v0.2)
-- Spec storage, GM command bridge, server response parsing, operation chains.
--
-- Server contract (src/game/Commands/SpecCommands.cpp):
--     .spec save <n>   -> "Saved spec %u: %u talents, %u action buttons."
--     .spec load <n>   -> "Spec %u loaded: %u/%u talents applied, %u/%u buttons placed."
--                      -> "Not while in combat."
--                      -> "Nothing stored for spec %u. Use .spec save %u first."
--     .spec bars <n>   -> "Restored %u of %u buttons for spec %u."
--     .spec init <f> <t> -> "Spec %u initialised. Stored %u talents into spec %u."
--     .spec respec <n> -> "Spec %u respecced. Talents reset."
--
-- All of these live under .spec, so they inherit its security level. The addon
-- deliberately does NOT call the GM command ".reset talents" -- that would
-- break for any non-GM account the moment .spec is opened up to players.
-- Nothing here requires a server change.

MSUI_DualSpec = MSUI_DualSpec or {}
MSUI_DualSpecDB = MSUI_DualSpecDB or {}

local D = MSUI_DualSpec

D.NUM_SPECS = 2
D.TIMEOUT = 8
D.SETTLE_DELAY = 2.5    -- seconds before we trust GetTalentTabInfo after a swap

-- While set, RefreshUI must NOT re-snapshot the active spec's tabs.
--
-- THE REAL RACE (v0.4): the server applies the new talents and sends the
-- learn/unlearn packets BEFORE the "Spec N loaded" system message arrives.
-- Those packets fire SPELLS_CHANGED and CHARACTER_POINTS_CHANGED on the
-- client, each of which calls RefreshUI. In that window db.active is still
-- the OUTGOING slot while GetTalentTabInfo already reports the INCOMING
-- build -- so the outgoing slot gets stamped with the incoming spread and
-- both cards converge. The guard must therefore be raised when the load is
-- SENT, not when its confirmation comes back.
D._settling = nil

-- transient state
D.pending = nil     -- { kind = "save"|"load"|"init", spec = n, thenLoad = n, step = k }
D.status = ""

-- ============================================================
-- Helpers
-- ============================================================

function D.Print(msg)
    DEFAULT_CHAT_FRAME:AddMessage("|cff00ccff[DualSpec]|r " .. tostring(msg))
end

function D.Error(msg)
    DEFAULT_CHAT_FRAME:AddMessage("|cffff4444[DualSpec]|r " .. tostring(msg))
end

function D.GM(cmd)
    SendChatMessage("." .. cmd, "SAY")
end

-- Strip WoW hyperlink / colour markup before pattern matching.
function D.Strip(s)
    if not s then return "" end
    s = string.gsub(s, "|c%x%x%x%x%x%x%x%x", "")
    s = string.gsub(s, "|r", "")
    s = string.gsub(s, "|H.-|h", "")
    s = string.gsub(s, "|h", "")
    return s
end

function D.OtherSpec(n)
    if n == 1 then return 2 end
    return 1
end

-- one-shot delayed call
function D.After(seconds, fn)
    local t = CreateFrame("Frame")
    t._e = 0
    t:SetScript("OnUpdate", function()
        t._e = t._e + (arg1 or 0.016)
        if t._e >= seconds then
            t:SetScript("OnUpdate", nil)
            fn()
        end
    end)
end

-- ============================================================
-- Saved variables
-- ============================================================

function D.DB()
    if not MSUI_DualSpecDB.specs then
        MSUI_DualSpecDB.specs = {}
    end
    if not MSUI_DualSpecDB.active then
        MSUI_DualSpecDB.active = 1
    end
    return MSUI_DualSpecDB
end

-- ============================================================
-- Talent tab snapshot (client side, display only)
-- ============================================================

function D.SnapshotTabs()
    local tabs = {}
    local num = GetNumTalentTabs()
    if not num or num < 1 then num = 3 end
    local i = 1
    while i <= num do
        local name, texture, points, fileName = GetTalentTabInfo(i)
        if name then
            tabs[i] = {
                name = name,
                texture = texture,
                points = points or 0,
                file = fileName,
            }
        end
        i = i + 1
    end
    return tabs
end

function D.Dominant(tabs)
    if not tabs then return nil end
    local best, bestPts, i = nil, -1, 1
    while tabs[i] do
        if tabs[i].points > bestPts then
            best = tabs[i]
            bestPts = tabs[i].points
        end
        i = i + 1
    end
    return best
end

function D.TotalPoints(tabs)
    if not tabs then return 0 end
    local total, i = 0, 1
    while tabs[i] do
        total = total + tabs[i].points
        i = i + 1
    end
    return total
end

function D.SpreadText(tabs)
    if not tabs then return "" end
    local out, i = "", 1
    while tabs[i] do
        if i > 1 then out = out .. " / " end
        out = out .. tabs[i].points
        i = i + 1
    end
    return out
end

-- Auto label from the dominant tree. Recomputed on every save unless the
-- player set a custom name.
function D.AutoName(tabs)
    local dom = D.Dominant(tabs)
    if dom and dom.points > 0 then return dom.name end
    return nil
end

function D.SpecLabel(n)
    local db = D.DB()
    local s = db.specs[n]
    if not s then return "Unused" end
    if s.name then return s.name end
    return "Spec " .. n
end

function D.Rename(n, text)
    local db = D.DB()
    if not db.specs[n] then
        D.Error("Spec " .. n .. " is not set up yet.")
        return
    end
    if not text or text == "" then
        -- clearing a custom name reverts to the automatic one
        db.specs[n].custom = nil
        db.specs[n].name = D.AutoName(db.specs[n].tabs)
    else
        db.specs[n].name = text
        db.specs[n].custom = 1
    end
    D.Print("Spec " .. n .. " is now \"" .. D.SpecLabel(n) .. "\".")
    if D.RefreshUI then D.RefreshUI() end
end

-- ============================================================
-- Timeout / status
-- ============================================================

local bus = CreateFrame("Frame", "MSUIDualSpecBus")
bus._elapsed = 0

function D.StartTimeout()
    bus._elapsed = 0
    bus:SetScript("OnUpdate", function()
        bus._elapsed = bus._elapsed + (arg1 or 0.016)
        if bus._elapsed >= D.TIMEOUT then
            bus:SetScript("OnUpdate", nil)
            if D.pending then
                D.Fail("No reply from the server. Does this account have access to .spec ?")
            end
        end
    end)
end

function D.StopTimeout()
    bus:SetScript("OnUpdate", nil)
end

function D.SetStatus(s)
    D.status = s or ""
    if D.RefreshUI then D.RefreshUI() end
end

function D.Fail(msg)
    D.pending = nil
    D.StopTimeout()
    D.status = ""
    D.Error(msg)
    if D.RefreshUI then D.RefreshUI() end
end

function D.Busy()
    return D.pending ~= nil
end

-- ============================================================
-- Primitive operations (no busy check - chains call these)
-- ============================================================

local function StoreLocal(n)
    local db = D.DB()
    db.specs[n] = db.specs[n] or {}
    db.specs[n].tabs = D.SnapshotTabs()
    if not db.specs[n].custom then
        db.specs[n].name = D.AutoName(db.specs[n].tabs)
    end
    -- Bars.lua owns the action bar layout (see Bars.lua for why it is client side)
    if D.CaptureBars then
        db.specs[n].bars = D.CaptureBars()
    end
end

local function SendSave(n)
    StoreLocal(n)
    D.GM("spec save " .. string.format("%d", n))
    D.StartTimeout()
end

local function SendLoad(n)
    D.GM("spec load " .. string.format("%d", n))
    D.StartTimeout()
end

-- ============================================================
-- Public operations
-- ============================================================

function D.Save(n, thenLoad)
    if D.Busy() then D.Error("Busy - wait for the current operation to finish.") return end
    D.pending = { kind = "save", spec = n, thenLoad = thenLoad }
    D.SetStatus("Storing " .. D.SpecLabel(n) .. "...")
    SendSave(n)
end

function D.Load(n)
    if D.Busy() then D.Error("Busy - wait for the current operation to finish.") return end
    local db = D.DB()
    if not db.specs[n] then D.Error("Spec " .. n .. " has no stored build yet.") return end
    if UnitAffectingCombat("player") then D.Error("Not while in combat.") return end
    D.pending = { kind = "load", spec = n }
    D._settling = 1          -- see the note on D._settling; raise it BEFORE sending
    D.SetStatus("Switching to " .. D.SpecLabel(n) .. "...")
    SendLoad(n)
end

-- Save the outgoing build first so talent spending since the last swap is not
-- lost, then load the target.
function D.SwitchTo(n)
    local db = D.DB()
    if n == db.active then return end
    if not db.specs[n] then
        D.Error(D.SpecLabel(n) .. " is not set up yet.")
        return
    end
    if UnitAffectingCombat("player") then D.Error("Not while in combat.") return end
    D.Save(db.active, n)
end

function D.Switch()
    local db = D.DB()
    D.SwitchTo(D.OtherSpec(db.active))
end

-- First use of an empty slot. The server does the whole thing atomically:
-- snapshot the live build into the active slot, wipe talents for free, then
-- claim the new slot empty. It refuses if the target already holds a build,
-- so it can only run once per slot.
function D.InitSlot(n)
    if D.Busy() then D.Error("Busy - wait for the current operation to finish.") return end
    local db = D.DB()
    if db.specs[n] then D.Error("Spec " .. n .. " already exists.") return end
    if UnitAffectingCombat("player") then D.Error("Not while in combat.") return end

    if not db.specs[db.active] then
        -- nothing stored anywhere yet: just claim the active slot first
        D.Save(db.active)
        return
    end

    -- One atomic server command. The old three-step chain called the GM
    -- command ".reset talents", which fails for a normal player once .spec
    -- is SEC_PLAYER. ".spec init" inherits .spec's security instead.
    db.specs[n] = { tabs = {}, bars = {} }
    D.pending = { kind = "init", spec = n }
    D._settling = 1
    D.SetStatus("Setting up slot " .. n .. "...")
    D.GM("spec init " .. string.format("%d", db.active) .. " " .. string.format("%d", n))
    D.StartTimeout()
end

-- Reset a single slot.
--   Active slot   -> wipe talents for free, then re-claim the slot with an
--                    empty build so you can spend the points again.
--   Inactive slot -> just forget the stored build; the slot goes back to
--                    "Empty Slot" and can be set up from scratch.
function D.ResetSlot(n)
    local db = D.DB()
    if not db.specs[n] then return end
    if UnitAffectingCombat("player") then D.Error("Not while in combat.") return end

    if n ~= db.active then
        db.specs[n] = nil
        D.Print("Slot " .. n .. " cleared. Click it to set it up again.")
        if D.RefreshUI then D.RefreshUI() end
        return
    end

    if D.Busy() then D.Error("Busy - wait for the current operation to finish.") return end

    db.specs[n].custom = nil
    db.specs[n].name = nil

    D.pending = { kind = "reset", spec = n }
    D._settling = 1
    D.SetStatus("Clearing talents...")
    D.GM("spec respec " .. string.format("%d", n))
    D.StartTimeout()
end

-- ============================================================
-- Server response parsing
-- ============================================================

function D.OnSystem(raw)
    local msg = D.Strip(raw)

    -- "Saved spec 1: 31 talents, 24 action buttons."
    local _, _, savedSpec = string.find(msg, "^Saved spec (%d+):")
    if savedSpec then
        local n = tonumber(savedSpec)
        local p = D.pending

        if p and p.kind == "reset" then
            D.pending = nil
            D._settling = nil
            D.StopTimeout()
            D.status = ""
            D.Print("Slot " .. p.spec .. " reset. Spend your points, then rename " ..
                    "it by right-clicking the card.")
            if D.RefreshUI then D.RefreshUI() end
            return
        end

        if p and p.kind == "save" and p.spec == n then
            D.pending = nil
            D.StopTimeout()
            D.status = ""
            if p.thenLoad then
                D.Load(p.thenLoad)
            else
                D.Print("Stored current build as " .. D.SpecLabel(n) .. ".")
                if D.RefreshUI then D.RefreshUI() end
            end
        else
            -- saved outside the addon - keep our copy honest
            StoreLocal(n)
            if D.RefreshUI then D.RefreshUI() end
        end
        return
    end

    -- "Spec 2 initialised. Stored 31 talents into spec 1."
    local _, _, initSpec = string.find(msg, "^Spec (%d+) initialised")
    if initSpec then
        local n = tonumber(initSpec)
        local db = D.DB()
        db.active = n
        db.specs[n] = db.specs[n] or {}
        db.specs[n].tabs = {}
        db.specs[n].custom = nil
        db.specs[n].name = nil
        D.pending = nil
        D.StopTimeout()
        D.status = ""
        D.After(D.SETTLE_DELAY, function()
            D._settling = nil
            if D.RefreshUI then D.RefreshUI() end
        end)
        D.Print("Slot " .. n .. " is yours - spend your points, then rename " ..
                "it by right-clicking the card.")
        if D.RefreshUI then D.RefreshUI() end
        return
    end

    -- "Spec 2 respecced. Talents reset."
    local _, _, respecSpec = string.find(msg, "^Spec (%d+) respecced")
    if respecSpec then
        local n = tonumber(respecSpec)
        local db = D.DB()
        db.specs[n] = db.specs[n] or {}
        db.specs[n].tabs = {}
        db.specs[n].custom = nil
        db.specs[n].name = nil
        D.pending = nil
        D.StopTimeout()
        D.status = ""
        D.After(D.SETTLE_DELAY, function()
            D._settling = nil
            if D.RefreshUI then D.RefreshUI() end
        end)
        D.Print("Slot " .. n .. " reset. Spend your points again.")
        if D.RefreshUI then D.RefreshUI() end
        return
    end

    -- "Spec 2 already has a stored build."
    if string.find(msg, "^Spec %d+ already has a stored build") then
        D.Fail("That slot already has a build stored. Use its Reset button instead.")
        return
    end

    -- "Not while dead." / "Not while casting."
    if string.find(msg, "^Not while dead") then
        D.Fail("Not while dead.")
        return
    end
    if string.find(msg, "^Not while casting") then
        D.Fail("Not while casting.")
        return
    end

    -- "Spec 2 loaded: 31/31 talents applied, 24/24 buttons placed."
    local _, _, loadSpec, applied, wanted =
        string.find(msg, "^Spec (%d+) loaded: (%d+)/(%d+) talents applied")
    if loadSpec then
        local n = tonumber(loadSpec)
        D.pending = nil
        D.StopTimeout()
        D.status = ""

        local db = D.DB()
        db.active = n
        db.specs[n] = db.specs[n] or {}

        -- Do NOT snapshot here. The stored tabs from this slot's last save are
        -- the authoritative display data; the client's live talent state is
        -- still catching up. Re-read it once things settle.
        D._settling = 1
        D.After(D.SETTLE_DELAY, function()
            D._settling = nil
            local d2 = D.DB()
            local cur = d2.specs[d2.active]
            if cur then
                cur.tabs = D.SnapshotTabs()
                if not cur.custom then
                    cur.name = D.AutoName(cur.tabs)
                end
            end
            if D.RefreshUI then D.RefreshUI() end
        end)

        if applied ~= wanted then
            D.Error("Only " .. applied .. " of " .. wanted ..
                    " talents applied - the stored build may be stale.")
        else
            D.Print("Now running " .. D.SpecLabel(n) .. ".")
        end

        if D.OnSwapComplete then D.OnSwapComplete(n) end
        if D.RefreshUI then D.RefreshUI() end
        return
    end

    if not D.pending then return end

    if string.find(msg, "^Not while in combat") then
        D.Fail("Not while in combat.")
        return
    end

    local _, _, emptySpec = string.find(msg, "^Nothing stored for spec (%d+)")
    if emptySpec then
        D.Fail("The server has nothing stored for spec " .. emptySpec .. ".")
        return
    end
end

-- ============================================================
-- Events
-- ============================================================

bus:RegisterEvent("CHAT_MSG_SYSTEM")
bus:RegisterEvent("SPELLS_CHANGED")
bus:RegisterEvent("CHARACTER_POINTS_CHANGED")
bus:RegisterEvent("PLAYER_REGEN_DISABLED")
bus:RegisterEvent("PLAYER_REGEN_ENABLED")

bus:SetScript("OnEvent", function()
    if event == "CHAT_MSG_SYSTEM" then
        D.OnSystem(arg1)
    else
        if D.RefreshUI then D.RefreshUI() end
    end
end)

-- ============================================================
-- Slash commands
-- ============================================================

SLASH_MSUIDUALSPEC1 = "/dualspec"
SLASH_MSUIDUALSPEC2 = "/ds"
SlashCmdList["MSUIDUALSPEC"] = function(msg)
    msg = msg or ""
    local _, _, cmd, rest = string.find(msg, "^(%S*)%s*(.*)$")
    if not cmd then cmd = msg; rest = "" end
    cmd = string.lower(cmd)

    if cmd == "switch" or cmd == "" then
        D.Switch()

    elseif cmd == "save" then
        local n = tonumber(rest)
        if not n or n < 1 or n > D.NUM_SPECS then
            D.Error("Usage: /ds save <1-" .. D.NUM_SPECS .. ">")
        else
            D.Save(n)
        end

    elseif cmd == "load" then
        local n = tonumber(rest)
        if not n or n < 1 or n > D.NUM_SPECS then
            D.Error("Usage: /ds load <1-" .. D.NUM_SPECS .. ">")
        else
            D.SwitchTo(n)
        end

    elseif cmd == "name" then
        local _, _, slot, text = string.find(rest, "^(%d+)%s*(.*)$")
        local n = tonumber(slot)
        if not n or n < 1 or n > D.NUM_SPECS then
            D.Error("Usage: /ds name <n> <text>   (blank text restores the automatic name)")
        else
            D.Rename(n, text)
        end

    elseif cmd == "forget" then
        local n = tonumber(rest)
        local db = D.DB()
        if not n or n < 1 or n > D.NUM_SPECS then
            D.Error("Usage: /ds forget <1-" .. D.NUM_SPECS .. ">   (clears one slot)")
        else
            db.specs[n] = nil
            if db.active == n then db.active = D.OtherSpec(n) end
            D.Print("Slot " .. n .. " cleared. The server still has its copy; " ..
                    "setting the slot up again overwrites it.")
            if D.RefreshUI then D.RefreshUI() end
        end

    elseif cmd == "reset" then
        if string.lower(rest) ~= "confirm" then
            D.Error("This wipes every stored spec name, build and bar layout for " ..
                    "this character. Type: /ds reset confirm")
        else
            MSUI_DualSpecDB.specs = {}
            MSUI_DualSpecDB.active = 1
            D.pending = nil
            D.StopTimeout()
            D.status = ""
            D.Print("Wiped. Open your talents and click the first card to start over.")
            if D.RefreshUI then D.RefreshUI() end
        end

    elseif cmd == "status" then
        local db = D.DB()
        D.Print("Active: " .. D.SpecLabel(db.active) .. " (slot " .. db.active .. ")")
        local i = 1
        while i <= D.NUM_SPECS do
            local s = db.specs[i]
            if s then
                D.Print("  " .. i .. ": " .. D.SpecLabel(i) .. "  [" .. D.SpreadText(s.tabs) .. "]")
            else
                D.Print("  " .. i .. ": unused")
            end
            i = i + 1
        end

    elseif cmd == "debug" then
        -- Dumps what GetTalentTabInfo actually returns on this client, so the
        -- card artwork can be pointed at whichever field is real.
        local num = GetNumTalentTabs()
        D.Print("GetNumTalentTabs() = " .. tostring(num))
        local i = 1
        while i <= (num or 3) do
            local a, b, c, d = GetTalentTabInfo(i)
            D.Print(i .. ": name=" .. tostring(a) ..
                       "  tex=" .. tostring(b) ..
                       "  pts=" .. tostring(c) ..
                       "  file=" .. tostring(d))
            i = i + 1
        end

    else
        D.Print("/ds switch | save <n> | load <n> | name <n> <text>")
        D.Print("    forget <n> | reset confirm | status | debug")
        D.Print("|cff909090name with no text restores the automatic tree name|r")
    end
end
