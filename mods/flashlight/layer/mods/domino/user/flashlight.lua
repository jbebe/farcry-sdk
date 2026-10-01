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
	System:Log("flashlight: Init");
	self._type.Listen(self);
end;

function export:In()
	System:Log("flashlight: In");
	self._type.Listen(self);
end;

function export:ShutDown()
	if (self.Toggle ~= nil) then
		CScriptCallbackSystem_GetInstance():RemoveCallback(self.Player, self.Toggle);
		self.Toggle = nil;
	end;
	if (self.Spawn ~= nil) then
		CScriptCallbackSystem_GetInstance():RemoveCallback(self.Player, self.Spawn);
		self.Spawn = nil;
	end;
end;

-- Registers for the key on the local player, or for the player's spawn while it is not loaded yet.
function export:Listen()
	if (self.Toggle ~= nil) then
		return;
	end;

	local player = GetLocalPlayerId();
	local loaded = IsEntityLoaded(player);
	System:Log("flashlight: player " .. tostring(player) .. ", loaded " .. tostring(loaded));
	self.Player = player;
	if (loaded == 1) then
		self.Toggle = CScriptCallbackSystem_GetInstance():RegisterEventCallback(player, self, "OnToggle", "InputDominoMove");
		System:Log("flashlight: listening, callback " .. tostring(self.Toggle));
		return;
	end;

	if (self.Spawn == nil) then
		self.Spawn = CScriptCallbackSystem_GetInstance():RegisterOnSpawnCallback(player, self, "OnPlayerSpawn");
		System:Log("flashlight: waiting for the player, callback " .. tostring(self.Spawn));
	end;
end;

function export:OnPlayerSpawn()
	System:Log("flashlight: player spawned");
	CScriptCallbackSystem_GetInstance():RemoveCallback(self.Player, self.Spawn);
	self.Spawn = nil;
	self._type.Listen(self);
end;

function export:OnToggle(entity)
	System:Log("flashlight: pressed");
	CDominoSoundManager_GetInstance():PlaySound(self.Player, "0x004e1ccf", 15, self, "OnSound");
end;

function export:OnSound(reason)
end;

_compilerVersion = 3;
