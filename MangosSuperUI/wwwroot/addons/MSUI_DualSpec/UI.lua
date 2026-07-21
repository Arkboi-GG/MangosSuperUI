-- MangosSuperUI_DualSpec :: UI.lua   (v0.2)
-- A 384x512 pane welded to the right edge of the talent frame.
--
-- Blizzard_TalentUI is LOAD ON DEMAND in 1.12 (UIParent.lua -> TalentFrame_LoadUI).
-- The pane is therefore built on ADDON_LOADED, never by hooking ToggleTalentFrame -
-- TalentFrame.lua redefines that global on load and would eat the hook.
--
-- The pane is a CHILD of TalentFrame, so show/hide/strata/close all come for free.

local D = MSUI_DualSpec

-- ============================================================
-- Tunables - the only numbers you should need to touch
-- ============================================================

-- Horizontal seam. TalentFrame's art overhangs its right edge by 2px and the
-- Spellbook panel art starts flush at 0, so +2 butts them together.
D.ATTACH_X = 2
D.ATTACH_Y = 0

D.PANEL_W = 384
D.PANEL_H = 512

D.ART = {
    topLeft  = "Interface\\Spellbook\\UI-SpellbookPanel-TopLeft",
    topRight = "Interface\\Spellbook\\UI-SpellbookPanel-TopRight",
    botLeft  = "Interface\\Spellbook\\UI-SpellbookPanel-BotLeft",
    botRight = "Interface\\Spellbook\\UI-SpellbookPanel-BotRight",
}

D.ARROW_TEX = "Interface\\Buttons\\UI-SpellbookIcon-NextPage-Up"

-- Talent tree background art. TalentFrame.lua builds these as
--   "Interface\\TalentFrame\\" .. fileName .. "-TopLeft"
-- where fileName is the 4th return of GetTalentTabInfo (e.g. "WarriorFury").
-- Painted across the whole pane for the ACTIVE spec, in four pieces, exactly
-- the way TalentFrame draws its own tree background.
D.TREE_ART_BASE = "Interface\\TalentFrame\\"
D.TREE_ART_FALLBACK = "MageArcane"

D.BG_ALPHA = 0.75       -- whole-pane tree art for the ACTIVE spec

-- Inset of the tree art from the pane edges. The art is stretched to fill this
-- rect exactly, so it tucks UNDER the frame border with no gap on any side.
-- Vanilla has no texture clipping, so "cover and crop" is not available -
-- fill-the-opening is the closest thing, and scenic art hides the stretch.
D.BG_LEFT = 20
D.BG_TOP = 72
D.BG_RIGHT = 20
D.BG_BOTTOM = 18

-- Native size of a talent tree background: 320x384 across four pieces.
D.BG_ART_W = 320
D.BG_ART_H = 384

-- Portrait sits in the ring baked into UI-SpellbookPanel-TopLeft.
-- SpellBookFrame draws its own icon there at 58x58, offset (10,-8).
D.PORTRAIT_SIZE = 58
D.PORTRAIT_X = 10
D.PORTRAIT_Y = -8

-- Close button, mirroring the talent frame's own placement.
D.CLOSE_X = -28
D.CLOSE_Y = -8

D.CARD_W = 300
D.CARD_H = 96
D.CARD_TOP = -104       -- first card's offset from the pane top
D.CARD_GAP = -28        -- vertical gap between cards
-- The spellbook panel art has a fatter left edge than right, so geometric
-- centring on the frame reads as right-shifted. Nudge left to optically centre.
D.CARD_X = -12

-- Reset button sits on the card's right edge, vertically centred so it clears
-- both the point spread above and the hint text below.
D.RESET_X = -14

D.cards = {}

-- ============================================================
-- Construction
-- ============================================================

local function AddBorder(f)
    local t

    t = f:CreateTexture(nil, "BORDER")
    t:SetTexture(D.ART.topLeft)
    t:SetWidth(256); t:SetHeight(256)
    t:SetPoint("TOPLEFT", f, "TOPLEFT", 0, 0)

    t = f:CreateTexture(nil, "BORDER")
    t:SetTexture(D.ART.topRight)
    t:SetWidth(128); t:SetHeight(256)
    t:SetPoint("TOPRIGHT", f, "TOPRIGHT", 0, 0)

    t = f:CreateTexture(nil, "BORDER")
    t:SetTexture(D.ART.botLeft)
    t:SetWidth(256); t:SetHeight(256)
    t:SetPoint("BOTTOMLEFT", f, "BOTTOMLEFT", 0, 0)

    t = f:CreateTexture(nil, "BORDER")
    t:SetTexture(D.ART.botRight)
    t:SetWidth(128); t:SetHeight(256)
    t:SetPoint("BOTTOMRIGHT", f, "BOTTOMRIGHT", 0, 0)
end

-- Tree background for the WHOLE pane, showing the ACTIVE spec.
-- Geometry copied from TalentFrame.xml's own background block:
--   TopLeft     256x256 at TOPLEFT (23,-77)
--   TopRight     64x256 anchored to TopLeft's TOPRIGHT
--   BottomLeft  256x128 anchored to TopLeft's BOTTOMLEFT
--   BottomRight  64x128 anchored to TopLeft's BOTTOMRIGHT
local function AddTreeBackground(f)
    local regionW = D.PANEL_W - D.BG_LEFT - D.BG_RIGHT
    local regionH = D.PANEL_H - D.BG_TOP - D.BG_BOTTOM
    local sx = regionW / D.BG_ART_W
    local sy = regionH / D.BG_ART_H

    local tl = f:CreateTexture(nil, "ARTWORK")
    tl:SetWidth(256 * sx); tl:SetHeight(256 * sy)
    tl:SetPoint("TOPLEFT", f, "TOPLEFT", D.BG_LEFT, -D.BG_TOP)

    local tr = f:CreateTexture(nil, "ARTWORK")
    tr:SetWidth(64 * sx); tr:SetHeight(256 * sy)
    tr:SetPoint("TOPLEFT", tl, "TOPRIGHT", 0, 0)

    local bl = f:CreateTexture(nil, "ARTWORK")
    bl:SetWidth(256 * sx); bl:SetHeight(128 * sy)
    bl:SetPoint("TOPLEFT", tl, "BOTTOMLEFT", 0, 0)

    local br = f:CreateTexture(nil, "ARTWORK")
    br:SetWidth(64 * sx); br:SetHeight(128 * sy)
    br:SetPoint("TOPLEFT", tl, "BOTTOMRIGHT", 0, 0)

    D.bg = { tl = tl, tr = tr, bl = bl, br = br }
end

function D.SetTreeBackground(file)
    if not D.bg then return end
    if not file then file = D.TREE_ART_FALLBACK end
    local base = D.TREE_ART_BASE .. file .. "-"
    D.bg.tl:SetTexture(base .. "TopLeft")
    D.bg.tr:SetTexture(base .. "TopRight")
    D.bg.bl:SetTexture(base .. "BottomLeft")
    D.bg.br:SetTexture(base .. "BottomRight")
    D.bg.tl:SetAlpha(D.BG_ALPHA)
    D.bg.tr:SetAlpha(D.BG_ALPHA)
    D.bg.bl:SetAlpha(D.BG_ALPHA)
    D.bg.br:SetAlpha(D.BG_ALPHA)
end

local function CardTooltip()
    local tabs = this.tabs
    GameTooltip:SetOwner(this, "ANCHOR_RIGHT")
    GameTooltip:SetText(this.labelText or "Unused", 1, 0.82, 0)
    if tabs then
        local i = 1
        while tabs[i] do
            GameTooltip:AddDoubleLine(tabs[i].name, tabs[i].points,
                                      1, 1, 1, 0.6, 0.8, 1.0)
            i = i + 1
        end
    else
        GameTooltip:AddLine("No build stored in this slot.", 1, 1, 1, true)
    end
    if this.hintText then
        GameTooltip:AddLine(" ")
        GameTooltip:AddLine(this.hintText, 0.4, 1.0, 0.4, true)
    end
    if this.tabs then
        GameTooltip:AddLine("Right-click to rename.", 0.6, 0.6, 0.6, true)
    end
    GameTooltip:Show()
end

local function CommitRename()
    local card = this:GetParent()
    D.Rename(card.specIndex, this:GetText())
    this:ClearFocus()
    this:Hide()
    card.label:Show()
end

local function CancelRename()
    local card = this:GetParent()
    this:ClearFocus()
    this:Hide()
    card.label:Show()
end

local function MakeCard(parent, index)
    local c = CreateFrame("Button", "MSUIDualSpecCard" .. index, parent)
    c:SetWidth(D.CARD_W)
    c:SetHeight(D.CARD_H)
    c:EnableMouse(true)
    c:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    c.specIndex = index

    -- The standard vanilla inset: tooltip background tiled inside a tooltip
    -- border. This is what Blizzard uses for every sunken panel in the 1.12 UI,
    -- so it reads as native rather than as a flat coloured rectangle.
    c:SetBackdrop({
        bgFile = "Interface\\Tooltips\\UI-Tooltip-Background",
        edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
        tile = true, tileSize = 16, edgeSize = 16,
        insets = { left = 5, right = 5, top = 5, bottom = 5 },
    })

    local label = c:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
    label:SetPoint("TOPLEFT", c, "TOPLEFT", 16, -14)
    label:SetJustifyH("LEFT")
    c.label = label

    local edit = CreateFrame("EditBox", "MSUIDualSpecRename" .. index, c, "InputBoxTemplate")
    edit:SetWidth(180)
    edit:SetHeight(20)
    edit:SetPoint("TOPLEFT", c, "TOPLEFT", 22, -12)
    edit:SetAutoFocus(false)
    edit:SetMaxLetters(24)
    edit:Hide()
    edit:SetScript("OnEnterPressed", CommitRename)
    edit:SetScript("OnEscapePressed", CancelRename)
    c.edit = edit

    local spread = c:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
    spread:SetPoint("TOPLEFT", label, "BOTTOMLEFT", 0, -8)
    spread:SetJustifyH("LEFT")
    c.spread = spread

    local hint = c:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    hint:SetPoint("BOTTOMLEFT", c, "BOTTOMLEFT", 16, 10)
    hint:SetWidth(D.CARD_W - 110)
    hint:SetJustifyH("LEFT")
    hint:SetTextColor(0.65, 0.65, 0.65)
    c.hint = hint

    local badge = c:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    badge:SetPoint("TOPRIGHT", c, "TOPRIGHT", -14, -14)
    badge:SetTextColor(1.0, 0.82, 0.0)
    c.badge = badge

    local arrow = c:CreateTexture(nil, "OVERLAY")
    arrow:SetTexture(D.ARROW_TEX)
    arrow:SetWidth(32); arrow:SetHeight(32)
    arrow:SetPoint("RIGHT", c, "LEFT", 4, 0)
    arrow:Hide()
    c.arrow = arrow

    local reset = CreateFrame("Button", "MSUIDualSpecReset" .. index, c, "UIPanelButtonTemplate")
    reset:SetWidth(64)
    reset:SetHeight(18)
    reset:SetPoint("RIGHT", c, "RIGHT", D.RESET_X, 0)
    reset:SetText("Reset")
    reset:SetScript("OnClick", function()
        D._resetTarget = this:GetParent().specIndex
        StaticPopup_Show("MSUI_DUALSPEC_RESET", D.SpecLabel(D._resetTarget))
    end)
    c.reset = reset

    c:SetScript("OnEnter", CardTooltip)
    c:SetScript("OnLeave", function() GameTooltip:Hide() end)
    c:SetScript("OnClick", function()
        local n = this.specIndex
        local db = D.DB()

        if arg1 == "RightButton" then
            if not db.specs[n] then return end
            this.label:Hide()
            this.edit:SetText(D.SpecLabel(n))
            this.edit:Show()
            this.edit:SetFocus()
            this.edit:HighlightText()
            return
        end

        -- Switching is the bottom button's job. A card click only sets up an
        -- empty slot; clicking a built spec does nothing.
        if not db.specs[n] then
            D.ConfirmInit(n)
        end
    end)

    return c
end

-- ============================================================
-- Confirm dialog for first use of an empty slot
-- ============================================================

StaticPopupDialogs["MSUI_DUALSPEC_INIT"] = {
    text = "Set up this specialization?\n\nYour current build is stored first, then your talents are wiped so you can spend them fresh. No respec cost.",
    button1 = "Set Up",
    button2 = "Cancel",
    OnAccept = function()
        if D._initTarget then
            D.InitSlot(D._initTarget)
            D._initTarget = nil
        end
    end,
    OnCancel = function()
        D._initTarget = nil
    end,
    timeout = 0,
    whileDead = 0,
    hideOnEscape = 1,
}

StaticPopupDialogs["MSUI_DUALSPEC_RESET"] = {
    text = "Reset %s?\n\nThis clears the stored build for that slot. If it is your active spec your talents are wiped too, at no cost, so you can spend them again.",
    button1 = "Reset",
    button2 = "Cancel",
    OnAccept = function()
        if D._resetTarget then
            D.ResetSlot(D._resetTarget)
            D._resetTarget = nil
        end
    end,
    OnCancel = function()
        D._resetTarget = nil
    end,
    timeout = 0,
    whileDead = 0,
    hideOnEscape = 1,
}

function D.ConfirmInit(n)
    D._initTarget = n
    StaticPopup_Show("MSUI_DUALSPEC_INIT")
end

-- ============================================================
-- Panel
-- ============================================================

function D.BuildPanel()
    if D.panel then return end
    if not TalentFrame then return end

    local f = CreateFrame("Frame", "MSUIDualSpecFrame", TalentFrame)
    f:SetWidth(D.PANEL_W)
    f:SetHeight(D.PANEL_H)
    f:SetPoint("TOPLEFT", TalentFrame, "TOPRIGHT", D.ATTACH_X, D.ATTACH_Y)
    f:EnableMouse(true)
    AddBorder(f)
    AddTreeBackground(f)
    D.panel = f

    -- Fill the empty portrait ring baked into the panel art with the same
    -- player portrait the talent frame uses.
    --
    -- MUST be OVERLAY. The border art moved from ARTWORK down to BORDER when
    -- the tree background was added, which put it ABOVE a BACKGROUND-layer
    -- portrait and painted over it. OVERLAY keeps the face on top of both.
    local portrait = f:CreateTexture(nil, "OVERLAY")
    portrait:SetWidth(D.PORTRAIT_SIZE)
    portrait:SetHeight(D.PORTRAIT_SIZE)
    portrait:SetPoint("TOPLEFT", f, "TOPLEFT", D.PORTRAIT_X, D.PORTRAIT_Y)
    SetPortraitTexture(portrait, "player")
    f.portrait = portrait
    D.portrait = portrait

    -- Second close button. UIPanelCloseButton's built-in OnClick hides its own
    -- parent, which would only close this pane, so point it at TalentFrame.
    local close = CreateFrame("Button", "MSUIDualSpecCloseButton", f, "UIPanelCloseButton")
    close:SetPoint("TOPRIGHT", f, "TOPRIGHT", D.CLOSE_X, D.CLOSE_Y)
    close:SetScript("OnClick", function()
        HideUIPanel(TalentFrame)
    end)
    D.closeButton = close

    local title = f:CreateFontString(nil, "OVERLAY", "GameFontNormal")
    title:SetPoint("TOP", f, "TOP", 0, -18)
    title:SetText("Specialization")

    local i = 1
    while i <= D.NUM_SPECS do
        local c = MakeCard(f, i)
        if i == 1 then
            c:SetPoint("TOP", f, "TOP", D.CARD_X, D.CARD_TOP)
        else
            c:SetPoint("TOP", D.cards[i - 1], "BOTTOM", 0, D.CARD_GAP)
        end
        D.cards[i] = c
        i = i + 1
    end

    local btn = CreateFrame("Button", "MSUIDualSpecSwitchButton", f, "UIPanelButtonTemplate")
    btn:SetWidth(200)
    btn:SetHeight(24)
    btn:SetPoint("BOTTOM", f, "BOTTOM", 0, 116)
    btn:SetText("Switch Specialization")
    btn:SetScript("OnClick", function() D.Switch() end)
    D.switchButton = btn

    local status = f:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
    status:SetPoint("TOP", btn, "BOTTOM", 0, -8)
    status:SetWidth(300)
    status:SetJustifyH("CENTER")
    D.statusText = status

    local prevOnShow = TalentFrame:GetScript("OnShow")
    TalentFrame:SetScript("OnShow", function()
        if prevOnShow then prevOnShow() end
        D.RefreshUI()
    end)

    D.RefreshUI()
end

-- ============================================================
-- Refresh
-- ============================================================

function D.RefreshUI()
    if not D.panel then return end

    if D.portrait then
        SetPortraitTexture(D.portrait, "player")
    end

    local db = D.DB()

    -- Keep the active card tracking live talent spending -- but never during
    -- the post-swap settling window, or we stamp the incoming slot with the
    -- outgoing build and both cards go identical.
    if not D._settling and not D.Busy() and db.specs[db.active] then
        db.specs[db.active].tabs = D.SnapshotTabs()
        if not db.specs[db.active].custom then
            db.specs[db.active].name = D.AutoName(db.specs[db.active].tabs)
        end
    end

    local target = D.OtherSpec(db.active)
    local busy = D.Busy()
    local inCombat = UnitAffectingCombat("player")

    -- The whole pane wears the ACTIVE spec's tree art.
    local activeSpec = db.specs[db.active]
    local activeDom = nil
    if activeSpec then activeDom = D.Dominant(activeSpec.tabs) end
    if activeDom and activeDom.file then
        D.SetTreeBackground(activeDom.file)
    else
        D.SetTreeBackground(nil)
    end

    local i = 1
    while i <= D.NUM_SPECS do
        local c = D.cards[i]
        local s = db.specs[i]
        local isActive = (i == db.active)

        c.labelText = D.SpecLabel(i)
        c.tabs = nil
        c.hintText = nil

        if s then
            c.label:SetText(D.SpecLabel(i))
            c.spread:SetText(D.SpreadText(s.tabs) .. "   |cff909090(" ..
                             D.TotalPoints(s.tabs) .. " spent)|r")
            c.tabs = s.tabs
            if isActive then
                c.hint:SetText("Your live build. Stored automatically on swap.")
            else
                c.hint:SetText("Use the button below to switch.")
                c.hintText = "Use Switch Specialization below to activate this build."
            end
        else
            c.label:SetText("Empty Slot")
            c.spread:SetText("")
            c.hint:SetText("Click to set up a second specialization.")
            c.hintText = "Stores your current build, then wipes talents so you can spend them fresh. Free."
        end

        if isActive then
            c.badge:SetText("ACTIVE")
            c:SetBackdropColor(0.16, 0.12, 0.03, 0.85)
            c:SetBackdropBorderColor(1.0, 0.82, 0.0, 1.0)
            c.label:SetTextColor(1, 0.82, 0)
            c.arrow:Hide()
        else
            c.badge:SetText("")
            c:SetBackdropColor(0.03, 0.03, 0.03, 0.85)
            c:SetBackdropBorderColor(0.45, 0.42, 0.35, 1.0)
            c.label:SetTextColor(0.75, 0.75, 0.75)
            if i == target and s and not busy then
                c.arrow:Show()
            else
                c.arrow:Hide()
            end
        end

        if s and not busy then
            c.reset:Show()
            c.reset:Enable()
        elseif s then
            c.reset:Show()
            c.reset:Disable()
        else
            c.reset:Hide()
        end

        if busy then c:Disable() else c:Enable() end

        i = i + 1
    end

    -- Switch button
    local canSwitch = (db.specs[target] ~= nil) and not busy and not inCombat
    if db.specs[target] then
        local tName = D.SpecLabel(target)
        if tName == D.SpecLabel(db.active) then
            -- both slots ended up with the same label; make the button honest
            tName = tName .. " (" .. target .. ")"
        end
        D.switchButton:SetText("Switch to " .. tName)
    else
        D.switchButton:SetText("Switch Specialization")
    end
    if canSwitch then D.switchButton:Enable() else D.switchButton:Disable() end

    -- Status line
    if D.status and D.status ~= "" then
        D.statusText:SetText(D.status)
        D.statusText:SetTextColor(1, 0.82, 0)
    elseif inCombat then
        D.statusText:SetText("Cannot swap in combat.")
        D.statusText:SetTextColor(1, 0.3, 0.3)
    elseif not db.specs[target] then
        D.statusText:SetText("Set up the second slot to enable swapping.")
        D.statusText:SetTextColor(0.6, 0.6, 0.6)
    else
        D.statusText:SetText("")
    end
end

-- ============================================================
-- Load gating - Blizzard_TalentUI is load on demand
-- ============================================================

local loader = CreateFrame("Frame", "MSUIDualSpecLoader")
loader:RegisterEvent("ADDON_LOADED")
loader:RegisterEvent("PLAYER_LOGIN")
loader:SetScript("OnEvent", function()
    if event == "ADDON_LOADED" then
        if arg1 == "Blizzard_TalentUI" then
            D.BuildPanel()
        end
    elseif event == "PLAYER_LOGIN" then
        if IsAddOnLoaded("Blizzard_TalentUI") then
            D.BuildPanel()
        end
    end
end)
