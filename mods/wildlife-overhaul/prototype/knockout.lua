WildlifeKnockout = WildlifeKnockout or {}
WildlifeKnockout._type = WildlifeKnockout

function WildlifeKnockout:Down(wake)
	self.player = GetLocalPlayerId()
	System:Log("WildlifeKnockout: Down " .. tostring(wake) .. " player " .. tostring(self.player))
	if wake then
		SetScriptedDeath(1)
		self.died = CScriptCallbackSystem_GetInstance():RegisterEventCallback(self.player, self, "Event_Died", "PlayerDied")
		System:Log("WildlifeKnockout: PlayerDied callback " .. tostring(self.died))
		self:After(3, "WakeUp")
	else
		BlockBuddyRescue()
		self:After(1, "Unblock")
	end
	SendPierceStimNoOrigin(self.player, 32)
	SendPierceStimNoOrigin(self.player, 32)
	System:Log("WildlifeKnockout: stims sent")
end

function WildlifeKnockout:After(seconds, method)
	local delays = CDominoDelayManager_GetInstance()
	delays:SendCommand(delays:CreateDelay(seconds, self, method), "start")
end

function WildlifeKnockout:Unblock()
	UnblockBuddyRescue()
end

function WildlifeKnockout:Event_Died()
	System:Log("WildlifeKnockout: PlayerDied")
end

function WildlifeKnockout:WakeUp()
	System:Log("WildlifeKnockout: WakeUp")
	local domino = CDominoManager_GetInstance()
	domino:SendCommandEventToEntity(self.player, "SetupPlayerForAnchorAnim", 1)
	local sent = domino:SendCommandEventToEntity2(self.player, "PlayAnim", self, "AnimCommandSent", "pawn_generic_desertgetup", 0, 1)
	System:Log("WildlifeKnockout: PlayAnim sent " .. tostring(sent))
	domino:SendRegisteredEventToEntity(self.player, "CFCXCountersComponentPlayerSP_HealingDone")
	SetScriptedDeath(0)
end

function WildlifeKnockout:AnimCommandSent()
	System:Log("WildlifeKnockout: AnimCommandSent")
end
