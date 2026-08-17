local ADDON_PREFIX = "AzerothCore"
local REFRESH_SECONDS = 5
local RESPONSE_TIMEOUT_SECONDS = 3
local EXPECTED_PROTOCOL = 7
local DEFAULT_WIDTH = 360
local DEFAULT_HEIGHT = 510
local MIN_WIDTH = 210
local MIN_HEIGHT = 110
local COMPACT_MIN_HEIGHT = 145
local MAX_WIDTH = 800
local MAX_HEIGHT = 900
local CLASS_NAMES = {
    [1] = "Warrior", [2] = "Paladin", [3] = "Hunter",
    [4] = "Rogue", [5] = "Priest", [6] = "Death Knight",
    [7] = "Shaman", [8] = "Mage", [9] = "Warlock",
    [11] = "Druid"
}

local requestCounter = 0
local activeRequest
local snapshot
local elapsedSinceRefresh = REFRESH_SECONDS
local loggedIn = false
local linePool = {}
local buttonPool = {}
local SendCompanionCommand
local carboniteInjectedPlayers = {}
local detailsExpanded = false
local expandedHeight = DEFAULT_HEIGHT
local applyingLayout = false
local SetDetailsExpanded

local function SplitTabs(value)
    local fields = {}
    local start = 1
    while true do
        local position = string.find(value, "\t", start, true)
        if not position then
            table.insert(fields, string.sub(value, start))
            return fields
        end
        table.insert(fields, string.sub(value, start, position - 1))
        start = position + 1
    end
end

local function EnsurePlayer(target, name)
    local player = target.players[name]
    if not player then
        player = { quests = {}, questOrder = {} }
        target.players[name] = player
    end
    return player
end

local function EnsureQuest(target, playerName, questId, title, complete)
    local player = EnsurePlayer(target, playerName)
    local quest = player.quests[questId]
    if not quest then
        quest = {
            id = questId,
            title = title or ("Quest " .. tostring(questId)),
            complete = complete,
            objectives = {},
            objectiveOrder = {}
        }
        player.quests[questId] = quest
        table.insert(player.questOrder, questId)
    else
        if title and title ~= "" then quest.title = title end
        if complete ~= nil then quest.complete = complete end
    end
    return quest
end

local function NormaliseObjectiveName(value)
    local normalised = string.gsub(value or "", "[^%w]", "")
    return string.lower(normalised)
end

local function CarbonitePartyQuestDisplayAvailable()
    if type(Nx) ~= "table" or type(Nx.Que) ~= "table"
        or type(Nx.Que.PaQ) ~= "table" or type(Nx.Que.ITQ) ~= "table"
        or type(Nx.Que.PUT) ~= "function" then
        return false
    end
    if not Nx.Que.GOp or not Nx.Que.GOp["QPartyShare"] then
        return false
    end
    local partyButton = Nx.Que.Wat and Nx.Que.Wat.BSP
    if partyButton and type(partyButton.GeP) == "function" then
        local succeeded, enabled = pcall(partyButton.GeP, partyButton)
        if not succeeded or not enabled then return false end
    end
    return true
end

local function CarboniteObjectiveNames(carboniteQuest)
    local names = {}
    if type(Nx.Que.UnO) ~= "function" then return names end
    for index = 1, 20 do
        local packedObjective = carboniteQuest[index + 3]
        if not packedObjective then break end
        local succeeded, objectiveName = pcall(
            Nx.Que.UnO, Nx.Que, packedObjective)
        names[index] = succeeded and NormaliseObjectiveName(objectiveName) or ""
    end
    return names
end

local function BuildCarboniteQuestProgress(quest, carboniteQuest)
    local progress = { Com2 = quest.complete and 1 or nil }
    local objectives = {}
    for _, objectiveKey in ipairs(quest.objectiveOrder) do
        table.insert(objectives, quest.objectives[objectiveKey])
    end

    local carboniteNames = CarboniteObjectiveNames(carboniteQuest)
    local slotCount = math.max(table.getn(objectives), table.getn(carboniteNames))
    for index = 1, slotCount do
        progress[index] = 0
        progress[index + 100] = 0
    end

    local assignedSlots = {}
    local unassignedObjectives = {}
    for _, objective in ipairs(objectives) do
        local objectiveName = NormaliseObjectiveName(objective.name)
        local matchedSlot
        if objectiveName ~= "" then
            for index, carboniteName in ipairs(carboniteNames) do
                if not assignedSlots[index] and carboniteName ~= ""
                    and (carboniteName == objectiveName
                        or string.find(carboniteName, objectiveName, 1, true)
                        or string.find(objectiveName, carboniteName, 1, true)) then
                    matchedSlot = index
                    break
                end
            end
        end
        if matchedSlot then
            assignedSlots[matchedSlot] = true
            progress[matchedSlot] = objective.current or 0
            progress[matchedSlot + 100] = objective.required or 0
        else
            table.insert(unassignedObjectives, objective)
        end
    end

    local nextSlot = 1
    for _, objective in ipairs(unassignedObjectives) do
        while assignedSlots[nextSlot] do nextSlot = nextSlot + 1 end
        assignedSlots[nextSlot] = true
        progress[nextSlot] = objective.current or 0
        progress[nextSlot + 100] = objective.required or 0
    end
    return progress
end

local function RefreshCarbonitePartyQuests(target)
    if not CarbonitePartyQuestDisplayAvailable() then return false end

    local activePlayers = {}
    local bridgedAny = false
    for _, companionName in ipairs(target.companionOrder) do
        local companion = target.companions[companionName]
        local player = target.players[companionName]
        companion.questsInCarbonite = false
        if companion.inParty and player then
            local carbonitePlayer = {}
            local supportedQuestCount = 0
            for _, questId in ipairs(player.questOrder) do
                local quest = player.quests[questId]
                local carboniteQuest = Nx.Que.ITQ[questId]
                if carboniteQuest then
                    carbonitePlayer[questId] = BuildCarboniteQuestProgress(
                        quest, carboniteQuest)
                    supportedQuestCount = supportedQuestCount + 1
                end
            end
            if supportedQuestCount == table.getn(player.questOrder) then
                Nx.Que.PaQ[companionName] = carbonitePlayer
                carboniteInjectedPlayers[companionName] = true
                activePlayers[companionName] = true
                companion.questsInCarbonite = true
                bridgedAny = true
            end
        end
    end

    for playerName in pairs(carboniteInjectedPlayers) do
        if not activePlayers[playerName] then
            Nx.Que.PaQ[playerName] = nil
            carboniteInjectedPlayers[playerName] = nil
        end
    end

    if Nx.Tim and type(Nx.Tim.Sta) == "function" then
        pcall(function()
            Nx.Tim:Sta("QPartyUpdate", 0, Nx.Que, Nx.Que.PUT)
        end)
    else
        pcall(Nx.Que.PUT, Nx.Que)
    end
    return bridgedAny
end

local frame = CreateFrame("Frame", "AzerothCompanionFrame", UIParent)
frame:SetWidth(DEFAULT_WIDTH)
frame:SetHeight(DEFAULT_HEIGHT)
frame:SetPoint("CENTER", UIParent, "CENTER", 300, 20)
frame:SetMovable(true)
frame:SetResizable(true)
frame:SetMinResize(MIN_WIDTH, MIN_HEIGHT)
frame:SetMaxResize(MAX_WIDTH, MAX_HEIGHT)
frame:EnableMouse(true)
frame:RegisterForDrag("LeftButton")
frame:SetClampedToScreen(true)
frame:SetBackdrop({
    bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
    edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
    tile = true, tileSize = 32, edgeSize = 32,
    insets = { left = 11, right = 12, top = 12, bottom = 11 }
})
frame:SetFrameStrata("MEDIUM")

local title = frame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
title:SetPoint("TOPLEFT", frame, "TOPLEFT", 18, -17)
title:SetText("Companions")

local status = frame:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
status:SetPoint("BOTTOMLEFT", frame, "BOTTOMLEFT", 18, 21)
status:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -112, 21)
status:SetHeight(12)
status:SetJustifyH("LEFT")
status:SetText("Waiting for login")

local closeButton = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
closeButton:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -5, -5)

local detailsButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate2")
detailsButton:SetWidth(62)
detailsButton:SetHeight(20)
detailsButton:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -34, -9)
detailsButton:SetText("Details")
detailsButton:SetScript("OnClick", function()
    if SetDetailsExpanded then SetDetailsExpanded(not detailsExpanded) end
end)

local refreshButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate2")
refreshButton:SetWidth(72)
refreshButton:SetHeight(22)
refreshButton:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -32, 15)
refreshButton:SetText("Refresh")

local resizeButton = CreateFrame("Button", nil, frame)
resizeButton:SetWidth(16)
resizeButton:SetHeight(16)
resizeButton:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -7, 7)
resizeButton:SetNormalTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Up")
resizeButton:SetPushedTexture("Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Down")
resizeButton:SetHighlightTexture(
    "Interface\\ChatFrame\\UI-ChatIM-SizeGrabber-Highlight")

local scroll = CreateFrame("ScrollFrame", "AzerothCompanionScrollFrame", frame,
    "UIPanelScrollFrameTemplate")
scroll:SetPoint("TOPLEFT", frame, "TOPLEFT", 19, -49)
scroll:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -34, 47)

local content = CreateFrame("Frame", nil, scroll)
content:SetWidth(DEFAULT_WIDTH - 58)
content:SetHeight(DEFAULT_HEIGHT - 100)
scroll:SetScrollChild(content)

local function Clamp(value, minimum, maximum)
    return math.min(maximum, math.max(minimum, value))
end

local function ContentViewportHeight()
    return math.max(1, scroll:GetHeight() - 4)
end

local function UpdateContentWidth()
    content:SetWidth(math.max(1, scroll:GetWidth() - 4))
end

local function SavePosition()
    if not AzerothCompanionDB then return end
    local point, _, relativePoint, x, y = frame:GetPoint(1)
    AzerothCompanionDB.point = point
    AzerothCompanionDB.relativePoint = relativePoint
    AzerothCompanionDB.x = x
    AzerothCompanionDB.y = y
end

local function SaveSize()
    if not AzerothCompanionDB then return end
    AzerothCompanionDB.width = math.floor(frame:GetWidth() + 0.5)
    if detailsExpanded then
        expandedHeight = math.floor(frame:GetHeight() + 0.5)
        AzerothCompanionDB.height = expandedHeight
    end
end

frame:SetScript("OnDragStart", function(self)
    self:StartMoving()
end)
frame:SetScript("OnDragStop", function(self)
    self:StopMovingOrSizing()
    SavePosition()
end)
closeButton:SetScript("OnClick", function()
    frame:Hide()
    if AzerothCompanionDB then AzerothCompanionDB.visible = false end
end)

resizeButton:SetScript("OnMouseDown", function(_, button)
    if button == "LeftButton" then frame:StartSizing("BOTTOMRIGHT") end
end)
resizeButton:SetScript("OnMouseUp", function()
    frame:StopMovingOrSizing()
    SaveSize()
end)

local function HideLines()
    for _, line in ipairs(linePool) do line:Hide() end
    for _, button in ipairs(buttonPool) do button:Hide() end
end

local function AddLine(index, text, red, green, blue, indent)
    local line = linePool[index]
    if not line then
        line = content:CreateFontString(nil, "ARTWORK", "GameFontNormalSmall")
        line:SetJustifyH("LEFT")
        line:SetJustifyV("TOP")
        line:SetNonSpaceWrap(true)
        linePool[index] = line
    end
    indent = indent or 0
    line:ClearAllPoints()
    line:SetPoint("TOPLEFT", content, "TOPLEFT", indent, content.nextY)
    line:SetWidth(math.max(1, content:GetWidth() - indent - 4))
    line:SetTextColor(red or 1, green or 1, blue or 1)
    line:SetText(text or "")
    line:Show()
    local height = math.max(15, line:GetStringHeight() + 3)
    content.nextY = content.nextY - height
    return index + 1
end

local function ObjectiveText(leaderName, companionName, leaderObjective,
    companionObjective)
    local companionCurrent = companionObjective and companionObjective.current or 0
    local companionRequired = companionObjective and companionObjective.required
        or leaderObjective.required
    return string.format("%s - You %d/%d | %s %d/%d",
        leaderObjective.name,
        leaderObjective.current, leaderObjective.required,
        companionName, companionCurrent, companionRequired)
end

local function CompanionObjectiveText(objective)
    return string.format("%s - %d/%d", objective.name,
        objective.current, objective.required)
end

local function AddCompanionButtons(target, companionName, companion, compact)
    local presetActions = {
        { "Quest", function()
            SendCompanionCommand("webadmin companion preset " .. target.leader
                .. " " .. companionName .. " questing")
        end },
        { "Tank", function()
            SendCompanionCommand("webadmin companion preset " .. target.leader
                .. " " .. companionName .. " dungeon-tank")
        end },
        { "Healer", function()
            SendCompanionCommand("webadmin companion preset " .. target.leader
                .. " " .. companionName .. " dungeon-healer")
        end }
    }
    local controlActions = {
        { "Follow", function()
            SendCompanionCommand(string.format(
                "webadmin companion behavior %s %s custom %s follow %s %.1f %d %d %d %d",
                target.leader, companionName, companion.role or "auto",
                companion.combatFocus or "assist", companion.followDistance or 3,
                companion.lootEnabled and 1 or 0,
                companion.gatherEnabled and 1 or 0,
                companion.autoSell and 1 or 0,
                companion.autoRepair and 1 or 0))
        end },
        { "Stay", function()
            SendCompanionCommand(string.format(
                "webadmin companion behavior %s %s custom %s stay %s %.1f %d %d %d %d",
                target.leader, companionName, companion.role or "auto",
                companion.combatFocus or "assist", companion.followDistance or 3,
                companion.lootEnabled and 1 or 0,
                companion.gatherEnabled and 1 or 0,
                companion.autoSell and 1 or 0,
                companion.autoRepair and 1 or 0))
        end },
        { "Regroup", function()
            SendCompanionCommand("webadmin companion regroup " .. target.leader
                .. " " .. companionName)
        end }
    }
    local actions = {}
    if not compact then
        for _, action in ipairs(presetActions) do table.insert(actions, action) end
    end
    for _, action in ipairs(controlActions) do table.insert(actions, action) end
    local gap = 4
    local buttonWidth = 58
    local perRow = math.max(2, math.floor(
        (content:GetWidth() - 12 + gap) / (buttonWidth + gap)))
    for actionIndex, action in ipairs(actions) do
        content.nextButton = content.nextButton + 1
        local button = buttonPool[content.nextButton]
        if not button then
            button = CreateFrame("Button", nil, content, "UIPanelButtonTemplate2")
            button:SetHeight(20)
            buttonPool[content.nextButton] = button
        end
        local zeroIndex = actionIndex - 1
        local column = math.mod(zeroIndex, perRow)
        local row = math.floor(zeroIndex / perRow)
        button:ClearAllPoints()
        button:SetPoint("TOPLEFT", content, "TOPLEFT",
            12 + column * (buttonWidth + gap), content.nextY - row * 23)
        button:SetWidth(buttonWidth)
        button:SetText(action[1])
        button:SetScript("OnClick", action[2])
        button:Show()
    end
    content.nextY = content.nextY
        - math.ceil(table.getn(actions) / perRow) * 23 - 2
end

local function Render(target)
    HideLines()
    content.nextY = -2
    content.nextButton = 0
    local lineIndex = 1
    if not target then
        lineIndex = AddLine(lineIndex,
            "No companion information has been received yet.", 0.7, 0.7, 0.7)
        content:SetHeight(ContentViewportHeight())
        return
    end
    if target.error then
        lineIndex = AddLine(lineIndex, target.error, 1, 0.3, 0.3)
    end
    if not target.protocolVersion then
        lineIndex = AddLine(lineIndex,
            "Server bridge is outdated: protocol version was not reported.",
            1, 0.65, 0.2)
    elseif target.protocolVersion < EXPECTED_PROTOCOL then
        lineIndex = AddLine(lineIndex,
            string.format("Server bridge v%d is outdated; addon requires v%d.",
                target.protocolVersion, EXPECTED_PROTOCOL), 1, 0.3, 0.3)
    elseif target.protocolVersion > EXPECTED_PROTOCOL then
        lineIndex = AddLine(lineIndex,
            string.format("Addon is outdated: server bridge v%d, addon supports v%d.",
                target.protocolVersion, EXPECTED_PROTOCOL), 1, 0.3, 0.3)
    end
    if #target.companionOrder == 0 then
        lineIndex = AddLine(lineIndex,
            "No active account companion was found.", 0.8, 0.8, 0.8)
        lineIndex = AddLine(lineIndex,
            "Start one from the website's Questing Companions page.",
            0.6, 0.6, 0.6)
    end

    local leader = target.players[target.leader]
    for _, companionName in ipairs(target.companionOrder) do
        local companion = target.companions[companionName]
        local className = CLASS_NAMES[companion.classId]
            or ("Class " .. tostring(companion.classId))
        lineIndex = AddLine(lineIndex,
            string.format("%s - Level %d %s | %s", companionName,
                companion.level, className, companion.role or "auto"),
            1, 0.82, 0)

        local bagColour = companion.freeSlots <= 3 and { 1, 0.25, 0.25 }
            or companion.freeSlots <= 8 and { 1, 0.75, 0.2 }
            or { 0.3, 1, 0.3 }
        local bagSummary = detailsExpanded
            and string.format("Bags: %d free / %d total | Loot: %s | Party: %s",
                companion.freeSlots, companion.totalSlots,
                companion.lootEnabled and "on" or "off",
                companion.inParty and "yes" or "no")
            or string.format("Bags %d/%d free | %s",
                companion.freeSlots, companion.totalSlots,
                companion.movement == "stay" and "staying" or "following")
        lineIndex = AddLine(lineIndex, bagSummary,
            bagColour[1], bagColour[2], bagColour[3], 12)
        if companion.maintenanceStatus and companion.maintenanceStatus ~= "" then
            lineIndex = AddLine(lineIndex,
                "Latest: " .. companion.maintenanceStatus,
                0.65, 0.85, 0.65, 12)
        end
        if detailsExpanded and (companion.autoSell or companion.autoRepair) then
            lineIndex = AddLine(lineIndex,
                "Maintenance: "
                    .. (companion.autoSell and "junk sales on" or "junk sales off")
                    .. " | "
                    .. (companion.autoRepair and "repairs on" or "repairs off"),
                0.55, 0.85, 0.55, 12)
        end
        if detailsExpanded and companion.role then
            lineIndex = AddLine(lineIndex,
                string.format("Behaviour: %s | %s | %s | %.0fm",
                    companion.role, companion.movement,
                    companion.combatFocus == "assist"
                        and "assist leader" or "defend party",
                    companion.followDistance or 3),
                0.7, 0.85, 1, 12)
        end
        if detailsExpanded and companion.gather and companion.gather ~= "" then
            lineIndex = AddLine(lineIndex,
                "Gathering: " .. companion.gather, 0.45, 0.75, 1, 12)
        end
        if detailsExpanded and companion.logisticsStatus
            and companion.logisticsStatus ~= "" then
            lineIndex = AddLine(lineIndex,
                string.format("Bag routes: %s | %d route%s | trigger %d / target %d",
                    companion.logisticsStatus,
                    companion.logisticsRouteCount or 0,
                    companion.logisticsRouteCount == 1 and "" or "s",
                    companion.logisticsTrigger or 4,
                    companion.logisticsTarget or 8),
                0.75, 0.75, 1, 12)
        end

        if detailsExpanded and not companion.questsInCarbonite then
            local companionPlayer = target.players[companionName]
            local shared = 0
            if leader and companionPlayer then
                for _, questId in ipairs(leader.questOrder) do
                    local leaderQuest = leader.quests[questId]
                    local companionQuest = companionPlayer.quests[questId]
                    if companionQuest then
                        shared = shared + 1
                        if shared == 1 then
                            lineIndex = AddLine(lineIndex, "Shared quests",
                                0.7, 0.85, 1, 12)
                        end
                        local complete = leaderQuest.complete and companionQuest.complete
                        lineIndex = AddLine(lineIndex,
                            (complete and "[Complete] " or "") .. leaderQuest.title,
                            complete and 0.3 or 1,
                            complete and 1 or 0.85,
                            complete and 0.3 or 0.25, 12)
                        for _, objectiveKey in ipairs(leaderQuest.objectiveOrder) do
                            local leaderObjective = leaderQuest.objectives[objectiveKey]
                            local companionObjective = companionQuest.objectives[objectiveKey]
                            local objectiveComplete = companionObjective
                                and companionObjective.current >= companionObjective.required
                            lineIndex = AddLine(lineIndex,
                                ObjectiveText(target.leader, companionName,
                                    leaderObjective, companionObjective),
                                objectiveComplete and 0.45 or 0.85,
                                objectiveComplete and 1 or 0.85,
                                objectiveComplete and 0.45 or 0.85, 24)
                        end
                    end
                end
            end

            local companionOnly = 0
            if companionPlayer then
                for _, questId in ipairs(companionPlayer.questOrder) do
                    local leaderHasQuest = leader and leader.quests[questId]
                    if not leaderHasQuest then
                        local quest = companionPlayer.quests[questId]
                        companionOnly = companionOnly + 1
                        if companionOnly == 1 then
                            lineIndex = AddLine(lineIndex, "Companion-only quests",
                                0.85, 0.7, 1, 12)
                        end
                        lineIndex = AddLine(lineIndex,
                            (quest.complete and "[Complete] " or "") .. quest.title,
                            quest.complete and 0.3 or 1,
                            quest.complete and 1 or 0.75,
                            quest.complete and 0.3 or 1, 12)
                        for _, objectiveKey in ipairs(quest.objectiveOrder) do
                            local objective = quest.objectives[objectiveKey]
                            local objectiveComplete =
                                objective.current >= objective.required
                            lineIndex = AddLine(lineIndex,
                                CompanionObjectiveText(objective),
                                objectiveComplete and 0.45 or 0.85,
                                objectiveComplete and 1 or 0.85,
                                objectiveComplete and 0.45 or 0.85, 24)
                        end
                    end
                end
            end
            if shared == 0 and companionOnly == 0 then
                lineIndex = AddLine(lineIndex, "No active quests.",
                    0.6, 0.6, 0.6, 12)
            elseif shared == 0 then
                lineIndex = AddLine(lineIndex, "No quests shared with you.",
                    0.6, 0.6, 0.6, 12)
            end
        end
        if target.protocolVersion == EXPECTED_PROTOCOL then
            AddCompanionButtons(target, companionName, companion,
                not detailsExpanded)
        end
        lineIndex = AddLine(lineIndex, " ", 1, 1, 1)
    end
    content:SetHeight(math.max(ContentViewportHeight(), -content.nextY + 8))
    if not detailsExpanded then
        local compactHeight = Clamp(-content.nextY + 96,
            COMPACT_MIN_HEIGHT, MAX_HEIGHT)
        if math.abs(frame:GetHeight() - compactHeight) > 1 then
            applyingLayout = true
            frame:SetHeight(compactHeight)
            applyingLayout = false
        end
    end
    if target.protocolVersion == EXPECTED_PROTOCOL then
        status:SetText("Bridge v" .. target.protocolVersion
            .. (target.carboniteQuestBridge and " + Carbonite" or "")
            .. " - " .. date("%H:%M:%S"))
    elseif target.protocolVersion then
        status:SetText("Version mismatch")
    else
        status:SetText("Server bridge outdated")
    end
end

SetDetailsExpanded = function(expanded)
    if expanded and not detailsExpanded then
        detailsExpanded = true
        if AzerothCompanionDB then AzerothCompanionDB.expanded = true end
        detailsButton:SetText("Compact")
        resizeButton:Show()
        applyingLayout = true
        frame:SetHeight(Clamp(expandedHeight, MIN_HEIGHT, MAX_HEIGHT))
        applyingLayout = false
    elseif not expanded and detailsExpanded then
        expandedHeight = frame:GetHeight()
        if AzerothCompanionDB then
            AzerothCompanionDB.height = math.floor(expandedHeight + 0.5)
            AzerothCompanionDB.expanded = false
        end
        detailsExpanded = false
        detailsButton:SetText("Details")
        resizeButton:Hide()
    elseif expanded then
        detailsButton:SetText("Compact")
        resizeButton:Show()
    else
        detailsButton:SetText("Details")
        resizeButton:Hide()
    end
    UpdateContentWidth()
    Render(snapshot)
end

frame:SetScript("OnSizeChanged", function()
    UpdateContentWidth()
    if not applyingLayout then Render(snapshot) end
end)

local function ParseProtocolLine(target, body)
    local fields = SplitTabs(body)
    local recordType = fields[1]
    if recordType == "WEBADMIN_COMPANION_PROTOCOL" and #fields >= 2 then
        target.protocolVersion = tonumber(fields[2])
    elseif recordType == "WEBADMIN_COMPANION" and #fields >= 8 then
        local name = fields[2]
        if not target.companions[name] then
            target.companions[name] = {}
            table.insert(target.companionOrder, name)
        end
        local companion = target.companions[name]
        companion.level = tonumber(fields[3]) or 0
        companion.classId = tonumber(fields[4]) or 0
        companion.inParty = fields[5] == "1"
        companion.lootEnabled = fields[6] == "1"
        companion.freeSlots = tonumber(fields[7]) or 0
        companion.totalSlots = tonumber(fields[8]) or 0
    elseif recordType == "WEBADMIN_COMPANION_GATHER" and #fields >= 3 then
        local companion = target.companions[fields[2]]
        if companion then companion.gather = fields[3] end
    elseif recordType == "WEBADMIN_COMPANION_LOGISTICS" and #fields >= 7 then
        local companion = target.companions[fields[2]]
        if companion then
            companion.logisticsTrigger = tonumber(fields[3]) or 4
            companion.logisticsTarget = tonumber(fields[4]) or 8
            companion.automaticLogistics = fields[5] == "1"
            companion.logisticsRouteCount = tonumber(fields[6]) or 0
            companion.logisticsStatus = fields[7]
        end
    elseif recordType == "WEBADMIN_COMPANION_MAINTENANCE" and #fields >= 4 then
        local companion = target.companions[fields[2]]
        if companion then
            companion.autoSell = fields[3] == "1"
            companion.autoRepair = fields[4] == "1"
        end
    elseif recordType == "WEBADMIN_COMPANION_MAINTENANCE_STATUS"
        and #fields >= 3 then
        local companion = target.companions[fields[2]]
        if companion then companion.maintenanceStatus = fields[3] end
    elseif recordType == "WEBADMIN_COMPANION_BEHAVIOR" and #fields >= 11 then
        local companion = target.companions[fields[2]]
        if companion then
            companion.preset = fields[3]
            companion.role = fields[4]
            companion.movement = fields[5]
            companion.combatFocus = fields[6]
            companion.followDistance = tonumber(fields[7]) or 3
            companion.lootEnabled = fields[8] == "1"
            companion.gatherEnabled = fields[9] == "1"
            companion.autoSell = fields[10] == "1"
            companion.autoRepair = fields[11] == "1"
        end
    elseif recordType == "WEBADMIN_COMPANION_QUEST" and #fields >= 5 then
        EnsureQuest(target, fields[2], tonumber(fields[3]) or 0,
            fields[5], fields[4] == "1")
    elseif recordType == "WEBADMIN_COMPANION_OBJECTIVE" and #fields >= 8 then
        local quest = EnsureQuest(target, fields[2], tonumber(fields[3]) or 0)
        local key = fields[4] .. ":" .. fields[5]
        if not quest.objectives[key] then
            table.insert(quest.objectiveOrder, key)
        end
        quest.objectives[key] = {
            kind = fields[4],
            entry = tonumber(fields[5]) or 0,
            current = tonumber(fields[6]) or 0,
            required = tonumber(fields[7]) or 0,
            name = fields[8]
        }
    elseif recordType == "WEBADMIN_COMPANION_SUMMARY" and #fields >= 3 then
        target.leader = fields[2]
        target.reportedCount = tonumber(fields[3]) or 0
        target.carboniteQuestBridge = RefreshCarbonitePartyQuests(target)
        snapshot = target
        target.completed = true
        Render(snapshot)
    elseif string.sub(recordType or "", 1, 9) ~= "WEBADMIN_" then
        target.error = body
    end
end

local function BeginRequest(command, kind)
    if not loggedIn or not UnitName("player") or not SendAddonMessage then
        return false
    end
    requestCounter = requestCounter + 1
    if requestCounter > 9999 then requestCounter = 1 end
    local requestId = string.format("%04d", requestCounter)
    activeRequest = {
        id = requestId,
        kind = kind,
        leader = UnitName("player"),
        players = {},
        companions = {},
        companionOrder = {},
        startedAt = GetTime(),
        completed = false
    }
    SendAddonMessage(ADDON_PREFIX,
        "i" .. requestId .. command,
        "WHISPER", UnitName("player"))
    elapsedSinceRefresh = 0
    return true
end

local function RequestSnapshot()
    if BeginRequest(
        "webadmin companion inspect " .. (UnitName("player") or ""),
        "snapshot") then
        status:SetText("Refreshing...")
    end
end

SendCompanionCommand = function(command)
    if BeginRequest(command, "command") then
        status:SetText("Applying companion command...")
    end
end

local function HandleAddonMessage(prefix, message)
    if prefix ~= ADDON_PREFIX or not activeRequest or not message then return end
    local opcode = string.sub(message, 1, 1)
    local requestId = string.sub(message, 2, 5)
    if requestId ~= activeRequest.id then return end
    if opcode == "m" and activeRequest.kind == "snapshot" then
        ParseProtocolLine(activeRequest, string.sub(message, 6))
    elseif opcode == "f" then
        activeRequest.error = activeRequest.error
            or "The server rejected the companion command."
        activeRequest.completed = true
        if activeRequest.kind == "snapshot" then
            snapshot = activeRequest
            Render(snapshot)
        end
        status:SetText("Request failed")
    elseif opcode == "o" then
        activeRequest.completed = true
        if activeRequest.kind == "command" then
            activeRequest = nil
            RequestSnapshot()
        elseif snapshot ~= activeRequest then
            snapshot = activeRequest
            Render(snapshot)
        end
    end
end

refreshButton:SetScript("OnClick", RequestSnapshot)

frame:RegisterEvent("ADDON_LOADED")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_LOGOUT")
frame:RegisterEvent("CHAT_MSG_ADDON")
frame:RegisterEvent("PARTY_MEMBERS_CHANGED")
frame:RegisterEvent("QUEST_LOG_UPDATE")
frame:RegisterEvent("BAG_UPDATE")
frame:SetScript("OnEvent", function(self, event, ...)
    if event == "ADDON_LOADED" then
        local loadedAddon = ...
        if loadedAddon ~= "AzerothCompanion" then return end
        AzerothCompanionDB = AzerothCompanionDB or {}
        expandedHeight = Clamp(AzerothCompanionDB.height or DEFAULT_HEIGHT,
            MIN_HEIGHT, MAX_HEIGHT)
        detailsExpanded = AzerothCompanionDB.expanded == true
        self:SetWidth(Clamp(AzerothCompanionDB.width or DEFAULT_WIDTH,
            MIN_WIDTH, MAX_WIDTH))
        self:SetHeight(detailsExpanded and expandedHeight or COMPACT_MIN_HEIGHT)
        if AzerothCompanionDB.point then
            self:ClearAllPoints()
            self:SetPoint(AzerothCompanionDB.point, UIParent,
                AzerothCompanionDB.relativePoint or AzerothCompanionDB.point,
                AzerothCompanionDB.x or 0, AzerothCompanionDB.y or 0)
        end
        if AzerothCompanionDB.visible == false then self:Hide() else self:Show() end
        if RegisterAddonMessagePrefix then
            RegisterAddonMessagePrefix(ADDON_PREFIX)
        end
        SetDetailsExpanded(detailsExpanded)
    elseif event == "PLAYER_LOGIN" then
        loggedIn = true
        elapsedSinceRefresh = REFRESH_SECONDS
        DEFAULT_CHAT_FRAME:AddMessage(
            "|cff33ff99Azeroth Companion|r loaded. Use /accomp to toggle the panel.")
    elseif event == "PLAYER_LOGOUT" then
        SavePosition()
        SaveSize()
    elseif event == "CHAT_MSG_ADDON" then
        local prefix, message = ...
        HandleAddonMessage(prefix, message)
    elseif loggedIn then
        elapsedSinceRefresh = math.max(elapsedSinceRefresh, REFRESH_SECONDS - 1)
    end
end)

frame:SetScript("OnUpdate", function(_, elapsed)
    if not loggedIn then return end
    if not frame:IsShown() and not CarbonitePartyQuestDisplayAvailable() then
        return
    end
    elapsedSinceRefresh = elapsedSinceRefresh + elapsed
    if activeRequest and not activeRequest.completed
        and GetTime() - activeRequest.startedAt >= RESPONSE_TIMEOUT_SECONDS then
        activeRequest.completed = true
        if activeRequest.kind == "command" then
            status:SetText("Companion command timed out")
            elapsedSinceRefresh = REFRESH_SECONDS
        else
            activeRequest.error = "No response from the companion server bridge. "
                .. "The module may be missing, outdated, or awaiting a rebuild."
            snapshot = activeRequest
            Render(snapshot)
            status:SetText("No server response")
        end
    end
    if elapsedSinceRefresh >= REFRESH_SECONDS then RequestSnapshot() end
end)

SLASH_AZEROTHCOMPANION1 = "/accomp"
SLASH_AZEROTHCOMPANION2 = "/companion"
SlashCmdList.AZEROTHCOMPANION = function()
    if frame:IsShown() then
        frame:Hide()
        AzerothCompanionDB.visible = false
    else
        frame:Show()
        AzerothCompanionDB.visible = true
        elapsedSinceRefresh = REFRESH_SECONDS
    end
end

SetDetailsExpanded(false)
