WildlifeKnockout = WildlifeKnockout or {}
WildlifeKnockout._type = WildlifeKnockout

function WildlifeKnockout:Down(wake)
	self.player = GetLocalPlayerId()
	if wake then
		SetScriptedDeath(1)
		self.died = CScriptCallbackSystem_GetInstance():RegisterEventCallback(self.player, self, "Event_Died", "PlayerDied")
	else
		BlockBuddyRescue()
		self:After(1, "Unblock")
	end
	SendPierceStimNoOrigin(self.player, 32)
	SendPierceStimNoOrigin(self.player, 32)
end

function WildlifeKnockout:Guard(step)
	local ok, err = pcall(step, self)
	if not ok then
		System:Log("WildlifeKnockout: " .. tostring(err))
	end
end

function WildlifeKnockout:After(seconds, method)
	local delays = CDominoDelayManager_GetInstance()
	delays:SendCommand(delays:CreateDelay(seconds, self, method), "start")
end

function WildlifeKnockout:Unblock()
	self:Guard(function() UnblockBuddyRescue() end)
end

function WildlifeKnockout:Event_Died()
	self:Guard(function(knockout)
		CScriptCallbackSystem_GetInstance():RemoveCallback(knockout.player, knockout.died)
		knockout:After(4, "WakeUp")
	end)
end

function WildlifeKnockout:WakeUp()
	self:Guard(function(knockout)
		local domino = CDominoManager_GetInstance()
		domino:SendCommandEventToEntity(knockout.player, "SetupPlayerForAnchorAnim", 1)
		domino:SendCommandEventToEntity2(knockout.player, "PlayAnim", knockout, "AnimCommandSent", "pawn_generic_desertgetup", 0, 1)
		domino:SendRegisteredEventToEntity(knockout.player, "CFCXCountersComponentPlayerSP_HealingDone")
		SetScriptedDeath(0)
	end)
end

function WildlifeKnockout:AnimCommandSent()
end
