local ADDON_PREFIX = "AzerothCore"
local REFRESH_SECONDS = 5
local RESPONSE_TIMEOUT_SECONDS = 3
local EXPECTED_PROTOCOL = 1
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

local frame = CreateFrame("Frame", "AzerothCompanionFrame", UIParent)
frame:SetWidth(440)
frame:SetHeight(510)
frame:SetPoint("CENTER", UIParent, "CENTER", 300, 20)
frame:SetMovable(true)
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
title:SetText("Questing companions")

local status = frame:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
status:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -72, -22)
status:SetJustifyH("RIGHT")
status:SetText("Waiting for login")

local closeButton = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
closeButton:SetPoint("TOPRIGHT", frame, "TOPRIGHT", -5, -5)

local refreshButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate2")
refreshButton:SetWidth(72)
refreshButton:SetHeight(22)
refreshButton:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -17, 15)
refreshButton:SetText("Refresh")

local scroll = CreateFrame("ScrollFrame", "AzerothCompanionScrollFrame", frame,
    "UIPanelScrollFrameTemplate")
scroll:SetPoint("TOPLEFT", frame, "TOPLEFT", 19, -49)
scroll:SetPoint("BOTTOMRIGHT", frame, "BOTTOMRIGHT", -34, 47)

local content = CreateFrame("Frame", nil, scroll)
content:SetWidth(382)
content:SetHeight(410)
scroll:SetScrollChild(content)

local function SavePosition()
    if not AzerothCompanionDB then return end
    local point, _, relativePoint, x, y = frame:GetPoint(1)
    AzerothCompanionDB.point = point
    AzerothCompanionDB.relativePoint = relativePoint
    AzerothCompanionDB.x = x
    AzerothCompanionDB.y = y
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

local function HideLines()
    for _, line in ipairs(linePool) do line:Hide() end
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
    line:SetWidth(378 - indent)
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

local function Render(target)
    HideLines()
    content.nextY = -2
    local lineIndex = 1
    if not target then
        lineIndex = AddLine(lineIndex,
            "No companion information has been received yet.", 0.7, 0.7, 0.7)
        content:SetHeight(410)
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
            string.format("%s - Level %d %s", companionName,
                companion.level, className), 1, 0.82, 0)

        local bagColour = companion.freeSlots <= 3 and { 1, 0.25, 0.25 }
            or companion.freeSlots <= 8 and { 1, 0.75, 0.2 }
            or { 0.3, 1, 0.3 }
        lineIndex = AddLine(lineIndex,
            string.format("Bags: %d free / %d total | Loot: %s | Party: %s",
                companion.freeSlots, companion.totalSlots,
                companion.lootEnabled and "on" or "off",
                companion.inParty and "yes" or "no"),
            bagColour[1], bagColour[2], bagColour[3], 12)
        if companion.gather and companion.gather ~= "" then
            lineIndex = AddLine(lineIndex,
                "Gathering: " .. companion.gather, 0.45, 0.75, 1, 12)
        end

        local companionPlayer = target.players[companionName]
        local shared = 0
        if leader and companionPlayer then
            for _, questId in ipairs(leader.questOrder) do
                local leaderQuest = leader.quests[questId]
                local companionQuest = companionPlayer.quests[questId]
                if companionQuest then
                    shared = shared + 1
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
        if shared == 0 then
            lineIndex = AddLine(lineIndex, "No shared quests.",
                0.6, 0.6, 0.6, 12)
        end
        lineIndex = AddLine(lineIndex, " ", 1, 1, 1)
    end
    content:SetHeight(math.max(410, -content.nextY + 8))
    if target.protocolVersion == EXPECTED_PROTOCOL then
        status:SetText("Bridge v" .. target.protocolVersion
            .. " - " .. date("%H:%M:%S"))
    elseif target.protocolVersion then
        status:SetText("Version mismatch")
    else
        status:SetText("Server bridge outdated")
    end
end

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
        snapshot = target
        target.completed = true
        Render(snapshot)
    elseif string.sub(recordType or "", 1, 9) ~= "WEBADMIN_" then
        target.error = body
    end
end

local function RequestSnapshot()
    if not loggedIn or not UnitName("player") or not SendAddonMessage then return end
    requestCounter = requestCounter + 1
    if requestCounter > 9999 then requestCounter = 1 end
    local requestId = string.format("%04d", requestCounter)
    activeRequest = {
        id = requestId,
        leader = UnitName("player"),
        players = {},
        companions = {},
        companionOrder = {},
        startedAt = GetTime(),
        completed = false
    }
    status:SetText("Refreshing...")
    SendAddonMessage(ADDON_PREFIX,
        "i" .. requestId .. "webadmin companion inspect " .. UnitName("player"),
        "WHISPER", UnitName("player"))
    elapsedSinceRefresh = 0
end

local function HandleAddonMessage(prefix, message)
    if prefix ~= ADDON_PREFIX or not activeRequest or not message then return end
    local opcode = string.sub(message, 1, 1)
    local requestId = string.sub(message, 2, 5)
    if requestId ~= activeRequest.id then return end
    if opcode == "m" then
        ParseProtocolLine(activeRequest, string.sub(message, 6))
    elseif opcode == "f" then
        activeRequest.error = activeRequest.error
            or "The server rejected the companion status request."
        snapshot = activeRequest
        activeRequest.completed = true
        Render(snapshot)
        status:SetText("Request failed")
    elseif opcode == "o" then
        activeRequest.completed = true
        if snapshot ~= activeRequest then
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
    elseif event == "PLAYER_LOGIN" then
        loggedIn = true
        elapsedSinceRefresh = REFRESH_SECONDS
        DEFAULT_CHAT_FRAME:AddMessage(
            "|cff33ff99Azeroth Companion|r loaded. Use /accomp to toggle the panel.")
    elseif event == "PLAYER_LOGOUT" then
        SavePosition()
    elseif event == "CHAT_MSG_ADDON" then
        local prefix, message = ...
        HandleAddonMessage(prefix, message)
    elseif loggedIn then
        elapsedSinceRefresh = math.max(elapsedSinceRefresh, REFRESH_SECONDS - 1)
    end
end)

frame:SetScript("OnUpdate", function(_, elapsed)
    if not loggedIn or not frame:IsShown() then return end
    elapsedSinceRefresh = elapsedSinceRefresh + elapsed
    if activeRequest and not activeRequest.completed
        and GetTime() - activeRequest.startedAt >= RESPONSE_TIMEOUT_SECONDS then
        activeRequest.completed = true
        activeRequest.error = "No response from the companion server bridge. "
            .. "The module may be missing, outdated, or awaiting a rebuild."
        snapshot = activeRequest
        Render(snapshot)
        status:SetText("No server response")
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

Render(nil)
