# VSS Vintorez

Replaces the single-player **Dart Rifle** with a VSS Vintorez: the VSS's mesh on the Dragunov's
skeleton and animation set, semi-automatic, a ten-round magazine off the sniper ammo pool, and the
Dragunov's jam and break behaviour in place of a weapon that never failed.

The mesh, the textures, the icons, the name, the shot sound and the reload all ship with it. Damage
stays the Dart Rifle's — that is what keeps it a stealth weapon rather than a battle rifle.

## Installing

This archive is a JackAll layer: `mods\` at its root is game content, and both installers read that
same shape.

- **Vortex** — with the Far Cry 2 extension installed, drop the zip in and enable it.
- **JackAll** — add the zip in the app, or from the command line:

```
jackall-cli mod build   --game "C:\Games\Far Cry 2" --layer vss-vintorez.zip
jackall-cli mod restore --game "C:\Games\Far Cry 2"
```

`mod restore` is the uninstall.

**Buy the weapon again after installing.** A weapon already in your inventory keeps the archetype it
was acquired with, so a save that already holds a Dart Rifle shows the new model on the old
behaviour.

## Known limitations

- Only English is renamed. The other ten languages still say "Dart Rifle" in the bazaar, the
  challenge list and the statistics.
- A dropped VSS in multiplayer has no barrel — the multiplayer pickup was deliberately skipped.

## How it was made

The whole procedure is written up, so you can do this to any other weapon:
<https://jbebe.github.io/farcry-sdk/docs/modding/replacing-a-weapon>

## Credit

This work is based on **"VSS «Винторез»"**
(https://sketchfab.com/3d-models/vss-b1ef04a89cd44300b082d952fea94957)
by **Zol4ik** (https://sketchfab.com/Zol4ik) licensed under
**CC-BY-4.0** (http://creativecommons.org/licenses/by/4.0/).
