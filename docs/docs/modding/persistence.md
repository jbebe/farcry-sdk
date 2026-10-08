---
sidebar_position: 14
---

# Keeping a mod's state

A mod can keep values in five places. They differ in what a value belongs to — your mod's install,
a playthrough, one entity, or every entity of an archetype — so choose by what the value is about.

| The value is about | Keep it in | Read and written by | Lasts |
|---|---|---|---|
| how the player set up your mod: a toggle, a slider | [FCSE settings](#fcse-settings-per-install) | C++ plugins, Lua scripts | across every save, in `bin\fcse.ini` |
| every entity of one archetype: a gun's tracer speed | [the archetype's data](#archetype-data-per-archetype) | the engine; plugins read entity-data keys | in your mod's files, never in a save |
| one entity: this gun's kill count | [FCSE entity data](#fcse-entity-data-per-entity) | C++ plugins | in that entity's save record |
| the playthrough: a mission flag, a campaign counter | [Domino `Globals`](#domino-globals-per-playthrough), or [the player's entity](#per-playthrough-from-a-plugin) for a plugin | Domino scripts; C++ plugins | in the save |
| this session only | the mod's own variables | the mod itself | until the game closes |

## FCSE settings: per install

`RegisterSettings` in a C++ plugin, `fcse.setting{...}` in a Lua script. FCSE stores the value in
`bin\fcse.ini` and shows it as a row in the Mod Configuration Menu. It belongs to the install, not to
a save, so loading a different save does not change it. See the
[FCSE README](https://github.com/jbebe/farcry-sdk/tree/main/tools/FCSE).

## Archetype data: per archetype

A value that is the same for every entity of an archetype belongs in the archetype itself, in the
entity library, shipped as a fragment in your mod's JackAll layer.

- **An engine property, if one exists.** Edit it in the archetype. Some properties are also saved
  per entity, and for an entity already in a save the saved value wins — see
  [mod compatibility](../file-formats/savegame.md#mod-compatibility-a-per-property-overlay-not-a-full-freeze).
- **An entity-data key otherwise.** Add FCSE's component to the archetype's `Components`:

  ```xml
  <object type="CFCSEDataComponent">
    <object type="MyMod.TracerSpeed">
      <value name="Float" type="Float">350</value>
    </object>
  </object>
  ```

  Each key is a child named after it, holding one `Int` (`Int32`), `Float` or `String` value. Every
  entity spawned from the archetype starts with these values, and a plugin reads them from any of
  those entities. A placed instance can override a key in its own sector data. They are never written
  into a save, so a later change to them reaches existing saves too.

Variants are separate archetypes: `weapons.Special.RPG7` and `weapons.Special.RPG7.Persistent` each
need the block, as does the archetype in each library the game reads — world1, world2 and the DLC
library. `jackall-cli mod lint` says whether the copy you edited is the one the game reads.

:::caution[Two mods, one archetype]
JackAll merges two layers' edits to one archetype line by line, so two mods that each add
`CFCSEDataComponent` to the same archetype are reported as a conflict. Until that merge pairs child
objects by tag, keep each archetype's entity data in one mod.
:::

## FCSE entity data: per entity

For a value about one particular entity, a C++ plugin uses `api->EntityData`:

```c
const FCSE_EntityDataAPI* data = api->EntityData;
void* entity = data->EntityOf(weapon);           // any component -> its CEntity
int32_t kills = 0;
data->GetInt(entity, "MyMod.Kills", &kills);     // false if absent
data->SetInt(entity, "MyMod.Kills", kills + 1);  // adds the component if the entity has none
```

A value set at runtime overrides the archetype's and is saved with the entity; `Remove` returns the
key to the archetype's value. Prefix your keys with your mod's name: every mod shares an entity's
keys.

A value reaches the save only if its entity does:

- The player's weapons and gear are saved.
- An entity nobody has touched has no save record. `Persist(entity)` asks the engine to keep it, as it
  does for the entities it carries between worlds; this has not been tried in game yet.
- An entity the game destroys takes its values with it, and one it later spawns afresh, such as a
  respawned guard, starts from its archetype's values.

A save or a data file that names the component still loads without FCSE; only these values are
lost. How the component is built is in [FCSE's entity data ABI](../engine-internals/fcse-entity-data-abi.md).

:::info[Seen in a running game]
Retail GOG v1.03: a value authored on `weapons.Secondary.Makarov` loaded from the entity library,
and values set on three weapons survived a save and reload.
:::

## Domino `Globals`: per playthrough

A Domino script keeps playthrough state in the Lua table `Globals`, which the game saves into every
save and writes back on load. The game's own story progress lives there, in
`Globals.MASTER_GameGlobals`. Give your mod a table of its own inside it:

```lua
Globals.MyMod = Globals.MyMod or {}
Globals.MyMod.Visits = (Globals.MyMod.Visits or 0) + 1
```

- Numbers keep six decimal places, strings and nested tables are kept, and booleans are not: use 0
  and 1, as the game does. Functions are not saved.
- It is one table per save, shared with the game's story state and every other mod's script: never
  replace `Globals` itself or another mod's table.
- FCSE's Lua scripts run in FCSE's own interpreter (LuaJIT), not the game's, and cannot see `Globals`.

How the engine saves it is in
[script state in the savegame](../engine-internals/domino-scripts.md#script-state-in-the-savegame).

## Per playthrough from a plugin

A C++ plugin cannot reach `Globals`. Keep playthrough values on the player's own entity with entity
data instead: it is always saved. FCSE has no call that returns the player yet, so a plugin finds the
player itself — FCSE's example plugin does it in the camera update, which is handed the player's pawn.
Whether the values follow the player across the story's change of world has not been tested.

## What does not last

- A plugin's or script's own variables: they are gone when the game closes.
- `bin\fcse.ini`, for anything about a playthrough: it is one file for every save.
- A file of your own next to the saves. A save's name is a random number, players copy and delete
  saves, and nothing ties your file to the right one.
