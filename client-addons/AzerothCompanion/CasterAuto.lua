-- Hunter-style filler casting for spellcasters on the matching AzerothCore server.
-- Every cast is driven by mod-web-admin; this addon only turns it on/off from a
-- real right-click and selects a learned filler spell.

local CANDIDATE_SPELLS = {
    MAGE = { "Frostbolt", "Fireball", "Arcane Missiles" },
    PRIEST = { "Smite", "Mind Flay", "Mind Blast" },
    WARLOCK = { "Shadow Bolt", "Incinerate", "Searing Pain" },
    DRUID = { "Wrath", "Starfire" },
    SHAMAN = { "Lightning Bolt", "Chain Lightning" },
    PALADIN = { "Exorcism", "Holy Shock" },
    DEATHKNIGHT = { "Icy Touch", "Death Coil" }
}

local addonEnabled = true
local knownFillers = {}
local selectedFiller
local autoRunning = false
local mouseDownX, mouseDownY, mouseDownAt
local statusButton

local function CharacterSettings()
    AzerothCompanionDB = AzerothCompanionDB or {}
    AzerothCompanionDB.casterAuto = AzerothCompanionDB.casterAuto or {}
    local key = (GetRealmName() or "realm") .. "-" .. (UnitName("player") or "player")
    AzerothCompanionDB.casterAuto[key] = AzerothCompanionDB.casterAuto[key] or {}
    return AzerothCompanionDB.casterAuto[key]
end

local function RankNumber(rank)
    return tonumber(string.match(rank or "", "(%d+)")) or 0
end

local function RefreshKnownFillers()
    local _, class = UnitClass("player")
    local wanted = CANDIDATE_SPELLS[class] or {}
    local wantedOrder = {}
    local found = {}
    for index, name in ipairs(wanted) do wantedOrder[name] = index end

    for tab = 1, GetNumSpellTabs() do
        local _, _, offset, count = GetSpellTabInfo(tab)
        for spellIndex = offset + 1, offset + count do
            local name, rank = GetSpellName(spellIndex, BOOKTYPE_SPELL)
            if name and wantedOrder[name] then
                local link = GetSpellLink(spellIndex, BOOKTYPE_SPELL)
                local spellId = link and tonumber(string.match(link, "spell:(%d+)"))
                if spellId then
                    local current = found[name]
                    if not current or RankNumber(rank) >= current.rankNumber then
                        found[name] = {
                            name = name,
                            rank = rank or "",
                            rankNumber = RankNumber(rank),
                            spellId = spellId,
                            icon = GetSpellTexture(spellIndex, BOOKTYPE_SPELL)
                        }
                    end
                end
            end
        end
    end

    knownFillers = {}
    for _, name in ipairs(wanted) do
        if found[name] then table.insert(knownFillers, found[name]) end
    end

    local settings = CharacterSettings()
    selectedFiller = nil
    for _, filler in ipairs(knownFillers) do
        if filler.name == settings.spellName then selectedFiller = filler end
    end
    selectedFiller = selectedFiller or knownFillers[1]
    if selectedFiller then settings.spellName = selectedFiller.name end
    addonEnabled = settings.enabled ~= false
end

local function IsHostileTarget()
    return UnitExists("target") and not UnitIsDead("target")
        and UnitCanAttack("player", "target")
end

local function StatusText()
    if not addonEnabled then return "OFF" end
    if autoRunning then return "AUTO" end
    return "READY"
end

local function UpdateButton()
    if not statusButton then return end
    if selectedFiller and addonEnabled then
        statusButton.icon:SetTexture(selectedFiller.icon)
        statusButton:Show()
    else
        statusButton:Hide()
        return
    end
    statusButton.label:SetText(StatusText())
    if autoRunning then
        statusButton:SetBackdropBorderColor(0.2, 1, 0.35, 1)
        statusButton.label:SetTextColor(0.35, 1, 0.45)
    elseif IsHostileTarget() then
        statusButton:SetBackdropBorderColor(0.85, 0.62, 0.2, 1)
        statusButton.label:SetTextColor(1, 0.82, 0.38)
    else
        statusButton:SetBackdropBorderColor(0.35, 0.35, 0.35, 1)
        statusButton.label:SetTextColor(0.72, 0.72, 0.72)
    end
end

local function ShowStatus(message, red, green, blue)
    if UIErrorsFrame then
        UIErrorsFrame:AddMessage(message, red or 1, green or 0.82, blue or 0.3, 1)
    end
end

local function ToggleCasterAuto()
    if not addonEnabled or not selectedFiller then return end
    if not IsHostileTarget() then
        ShowStatus("Caster Auto: select a hostile target first.", 1, 0.35, 0.3)
        return
    end
    SendChatMessage(".casterauto toggle " .. selectedFiller.spellId, "SAY")
end

local function StopCasterAuto()
    SendChatMessage(".casterauto stop", "SAY")
end

local function SelectFiller(index)
    if #knownFillers == 0 then return end
    if index < 1 then index = #knownFillers end
    if index > #knownFillers then index = 1 end
    selectedFiller = knownFillers[index]
    CharacterSettings().spellName = selectedFiller.name
    autoRunning = false
    StopCasterAuto()
    UpdateButton()
    ShowStatus("Caster Auto spell: " .. selectedFiller.name .. " " .. selectedFiller.rank)
end

local function CycleFiller()
    if #knownFillers == 0 then return end
    local current = 1
    for index, filler in ipairs(knownFillers) do
        if selectedFiller and filler.name == selectedFiller.name then current = index end
    end
    SelectFiller(current + 1)
end

local function CreateStatusButton()
    statusButton = CreateFrame("Button", "AzerothCasterAutoButton", TargetFrame)
    statusButton:SetWidth(38)
    statusButton:SetHeight(38)
    statusButton:SetPoint("TOPRIGHT", TargetFrame, "TOPRIGHT", 23, -3)
    statusButton:RegisterForClicks("LeftButtonUp", "RightButtonUp")
    statusButton:SetBackdrop({
        bgFile = "Interface\\Buttons\\WHITE8X8",
        edgeFile = "Interface\\Tooltips\\UI-Tooltip-Border",
        tile = false, edgeSize = 10, insets = { left = 2, right = 2, top = 2, bottom = 2 }
    })
    statusButton:SetBackdropColor(0.03, 0.04, 0.05, 0.92)
    statusButton.icon = statusButton:CreateTexture(nil, "ARTWORK")
    statusButton.icon:SetPoint("TOPLEFT", 4, -4)
    statusButton.icon:SetPoint("BOTTOMRIGHT", -4, 4)
    statusButton.label = statusButton:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
    statusButton.label:SetPoint("BOTTOM", statusButton, "BOTTOM", 0, 3)
    statusButton.label:SetShadowOffset(1, -1)
    statusButton:SetScript("OnClick", function(_, button)
        if button == "RightButton" then CycleFiller() else ToggleCasterAuto() end
    end)
    statusButton:SetScript("OnEnter", function(self)
        GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
        GameTooltip:SetText("Caster Auto-Attack")
        if selectedFiller then
            GameTooltip:AddLine(selectedFiller.name .. " " .. selectedFiller.rank, 1, 0.82, 0.35)
        end
        GameTooltip:AddLine("Right-click an enemy in the world to toggle.", 0.85, 0.85, 0.85)
        GameTooltip:AddLine("Left-click this icon to toggle; right-click it to change spell.", 0.7, 0.7, 0.7)
        GameTooltip:Show()
    end)
    statusButton:SetScript("OnLeave", function() GameTooltip:Hide() end)
    UpdateButton()
end

local eventFrame = CreateFrame("Frame")
eventFrame:RegisterEvent("PLAYER_LOGIN")
eventFrame:RegisterEvent("PLAYER_TARGET_CHANGED")
eventFrame:RegisterEvent("LEARNED_SPELL_IN_TAB")
eventFrame:RegisterEvent("CHARACTER_POINTS_CHANGED")
eventFrame:SetScript("OnEvent", function(_, event)
    if event == "PLAYER_LOGIN" then
        RefreshKnownFillers()
        CreateStatusButton()
        WorldFrame:HookScript("OnMouseDown", function(_, button)
            if button ~= "RightButton" then return end
            mouseDownX, mouseDownY = GetCursorPosition()
            mouseDownAt = GetTime()
        end)
        WorldFrame:HookScript("OnMouseUp", function(_, button)
            if button ~= "RightButton" or not mouseDownAt then return end
            local x, y = GetCursorPosition()
            local moved = math.abs(x - mouseDownX) + math.abs(y - mouseDownY)
            local held = GetTime() - mouseDownAt
            mouseDownAt = nil
            if moved <= 8 and held <= 0.45 and IsHostileTarget() then
                ToggleCasterAuto()
            end
        end)
    elseif event == "LEARNED_SPELL_IN_TAB" or event == "CHARACTER_POINTS_CHANGED" then
        RefreshKnownFillers()
        UpdateButton()
    elseif event == "PLAYER_TARGET_CHANGED" then
        UpdateButton()
        -- Start automatically when Tab (or another targeting action) selects a
        -- hostile target.  The explicit button remains available to stop or
        -- restart the behaviour, while avoiding duplicate toggle requests.
        if addonEnabled and selectedFiller and IsHostileTarget() and not autoRunning then
            SendChatMessage(".casterauto start " .. selectedFiller.spellId, "SAY")
        end
    end
end)

ChatFrame_AddMessageEventFilter("CHAT_MSG_SYSTEM", function(_, _, message)
    if not message or not string.find(message, "CASTERAUTO", 1, true) then return false end
    if string.find(message, "CASTERAUTO START", 1, true) then
        autoRunning = true
        ShowStatus("Caster Auto started", 0.35, 1, 0.45)
    elseif string.find(message, "CASTERAUTO STOP", 1, true) then
        autoRunning = false
        ShowStatus("Caster Auto stopped", 1, 0.72, 0.3)
    elseif string.find(message, "CASTERAUTO STATUS running", 1, true) then
        autoRunning = true
    elseif string.find(message, "CASTERAUTO STATUS stopped", 1, true) then
        autoRunning = false
    end
    UpdateButton()
    return true
end)

SLASH_AZEROTHCASTERAUTO1 = "/cauto"
SlashCmdList.AZEROTHCASTERAUTO = function(input)
    local command = string.lower(string.match(input or "", "^%s*(.-)%s*$"))
    if command == "off" then
        addonEnabled = false
        CharacterSettings().enabled = false
        autoRunning = false
        StopCasterAuto()
    elseif command == "on" then
        addonEnabled = true
        CharacterSettings().enabled = true
    elseif command == "stop" then
        StopCasterAuto()
    elseif command == "status" then
        SendChatMessage(".casterauto status", "SAY")
    elseif command == "next" then
        CycleFiller()
    elseif command ~= "" then
        for index, filler in ipairs(knownFillers) do
            if string.lower(filler.name) == command then SelectFiller(index) return end
        end
        ShowStatus("Unknown learned filler spell: " .. command, 1, 0.35, 0.3)
    else
        local spell = selectedFiller and (selectedFiller.name .. " " .. selectedFiller.rank)
            or "none available"
        DEFAULT_CHAT_FRAME:AddMessage("Caster Auto: " .. StatusText() .. " · " .. spell)
        DEFAULT_CHAT_FRAME:AddMessage("/cauto on | off | stop | status | next | <spell name>")
    end
    UpdateButton()
end
