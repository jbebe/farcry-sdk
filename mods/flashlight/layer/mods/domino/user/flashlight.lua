-- Flashlight: the toggle_flashlight control, sent as InputDominoMove, switches the flashlight.
-- Hosted by a COmniEntity in each world's omnis.fcb. Test 1 only answers the key with a click.

export = {
};

function export:LuaDependencies()
	return {
	};
end;

function export:Create(cbox)
end;

-- Init runs on every load, In only on the first: either may come before the player exists.
function export:Init()
	self._type.Listen(self);
end;

function export:In()
	self._type.Listen(self);
end;

function export:ShutDown()
	if (self.Toggle ~= nil) then
		CScriptCallbackSystem_GetInstance():RemoveCallback(self.Player, self.Toggle);
		self.Toggle = nil;
	end;
	if (self.Retry ~= nil) then
		CDominoDelayManager_GetInstance():RemoveDelay(self.Retry);
		self.Retry = nil;
	end;
end;

-- Registers for the key on the local player, retrying each second until the player is loaded.
function export:Listen()
	if (self.Toggle ~= nil) then
		return;
	end;

	local player = GetLocalPlayerId();
	if (player ~= nil and IsEntityLoaded(player) == 1) then
		self.Player = player;
		self.Toggle = CScriptCallbackSystem_GetInstance():RegisterEventCallback(player, self, "OnToggle", "InputDominoMove");
		System:Log("Flashlight: listening");
		return;
	end;

	if (self.Retry == nil) then
		self.Retry = CDominoDelayManager_GetInstance():CreateDelay(1, self, "OnRetry");
		CDominoDelayManager_GetInstance():SendCommand(self.Retry, "start");
	else
		CDominoDelayManager_GetInstance():SendCommand(self.Retry, "restart");
	end;
end;

function export:OnRetry()
	self._type.Listen(self);
end;

function export:OnToggle(entity)
	CDominoSoundManager_GetInstance():PlaySound(self.Player, "0x004e1ccf", 15, self, "OnSound");
end;

function export:OnSound(reason)
end;

_compilerVersion = 3;
