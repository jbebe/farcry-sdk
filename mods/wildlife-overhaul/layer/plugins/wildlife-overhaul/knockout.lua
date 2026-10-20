WildlifeKnockout = WildlifeKnockout or {}
WildlifeKnockout._type = WildlifeKnockout

function WildlifeKnockout:Faint()
	self.player = GetLocalPlayerId()
	SetScriptedDeath(1)
	self.died = CScriptCallbackSystem_GetInstance():RegisterEventCallback(self.player, self, "Event_Died", "PlayerDied")
	self:Hit()
end

function WildlifeKnockout:Die()
	self.player = GetLocalPlayerId()
	BlockBuddyRescue()
	self:After(1, "Unblock")
	self:Hit()
end

-- Two level-32 Pierce stims, about 3,200 health: more than any difficulty's full bar.
function WildlifeKnockout:Hit()
	SendPierceStimNoOrigin(self.player, 32)
	SendPierceStimNoOrigin(self.player, 32)
end

function WildlifeKnockout:After(seconds, method)
	local delays = CDominoDelayManager_GetInstance()
	delays:SendCommand(delays:CreateDelay(seconds, self, method), "start")
end

function WildlifeKnockout:Unblock()
	UnblockBuddyRescue()
end

function WildlifeKnockout:Event_Died()
	CScriptCallbackSystem_GetInstance():RemoveCallback(self.player, self.died)
	self:After(2, "WakeUp")
end

function WildlifeKnockout:WakeUp()
	local domino = CDominoManager_GetInstance()
	domino:SendCommandEventToEntity(self.player, "SetupPlayerForAnchorAnim", 1)
	self.stood = CScriptCallbackSystem_GetInstance():RegisterEventCallback(self.player, self, "Event_StoodUp", "FinishedPlayAnim")
	domino:SendCommandEventToEntity2(self.player, "PlayAnim", self, "AnimCommandSent", "pawn_generic_desertgetup", 0, 1)
	SetScriptedDeath(0)
end

function WildlifeKnockout:Event_StoodUp()
	CScriptCallbackSystem_GetInstance():RemoveCallback(self.player, self.stood)
	SetPlayerActionMap("default")
	WildlifeKnockdownRestore()
end

function WildlifeKnockout:AnimCommandSent()
end
