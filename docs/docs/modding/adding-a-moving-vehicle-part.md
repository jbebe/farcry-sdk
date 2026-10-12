---
sidebar_position: 7.6
---

# Adding a moving part to a vehicle

A part of a vehicle that moves by itself is a Havok body hinged to the chassis, as the retail doors
and hoods are. Examples are a radiator fan, a winch drum or a swinging sign. The game already draws a
hinged body wherever Havok puts it, so most of the work is data:

- a node in the model
- a body and a hinge in the `.hkx`
- a part in the entity

A plugin is needed only to decide when the part is hinged and to drive its motor.

:::tip[This procedure is a shipped mod]
The big truck's radiator fan in `mods/vehicle-overhaul` is built this way and was confirmed in game
on 2026-10-12. When the player starts the engine it spins up to 8 Hz in a second. After they get
out it coasts to a stop over eight seconds. The engine facts are traced in `FarCry2_server` and
Steam `Dunia.dll`, and [vehicle physics](../engine-internals/vehicle-physics.md#breakable-parts)
has them.
:::

## What went wrong, so you don't repeat it

Each of these symptoms cost at least one round in game. Each had a single cause, and in no case was
it the first guess.

| Symptom | Cause | Fix |
|---|---|---|
| The part is not drawn anywhere | Its bone was added only to the entity's baked descriptor, which the retail game never reads; the skeleton comes from the `.xbg`'s nodes | [A node in the model](#step-1--a-node-in-the-model) |
| Touching or shooting the truck launches it nose-first into the air | The part's hub sits inside the chassis's engine-block hull, and the contacts push the two apart | `RESPONSE_NONE` on the [body](#step-2--a-body-in-the-hkx) |
| On some loads the part is missing and the truck flies when touched; on others the truck jerks once and then all is well. Later, getting in spun the truck off into the sky | The body was appended after the `HIGH_` bodies. The game keeps a position, rotation and mass only for the bodies before the first `HIGH_` or `NAV_` one, so it built the hinged part from whatever memory followed | Insert the body [before the first `HIGH_` body](#step-2--a-body-in-the-hkx) |
| The plugin never found the player's car, then crashed reading its chassis | It took the frame body's vehicle object, a wrapper, for the physics object behind it | The physics object is at the wrapper's `+0x5C` ([the plugin](#step-5--the-plugin)) |
| The motor never switched off | The plugin looked for the hinge only among the constraints the chassis is slave of, but the game's hinge is in its master list | Search both lists |

The intermittent failures were first blamed on hinging the part while the level loads, which is why
the fan is hinged at engine start instead. That theory was wrong. With the body in the right place, a
part hinged from spawn as the doors are should work too, but this was not tried.

## Step 1 — a node in the model

The game builds a vehicle's skeleton from its `.xbg`'s nodes. Every frame it copies each hinged
body's transform, relative to the vehicle, onto the node of the part's graphic object. So the part
needs a node of its own, and its mesh must hang on that node.

Make the node a copy of `Chassis`, with the same parent and the same rest transform. The mesh is then
exported relative to the chassis and draws where it was modelled, as long as the body's origin also
sits on the chassis origin ([step 2](#step-2--a-body-in-the-hkx)).

The Blender add-on cannot add a node itself. The fan's node was added by a headless script, run as
`blender -b bigtruck.blend -P export_fan.py -- out.fc2model`. It marks the separated object as a new
part, runs the add-on's own export, and edits the mesh document before saving:

```python
import copy
import sys

sys.path.insert(0, r"C:\Projects\FarCry2\tools\BlenderFC2")
import bpy
from addon import export_xbg, import_xbg

if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")

fan = bpy.data.objects["CHASSIS_LOD0.011.fan"]
collection = next(c for c in fan.users_collection if import_xbg.PROP_SOURCE in c)
del fan[import_xbg.PROP_SUBMESH]
fan[import_xbg.PROP_NEW_PART] = "Fan"
import_xbg.stamp_placement(fan)

built = export_xbg.build_mesh(collection)
mesh, pack = built["mesh"], built["pack"]
nodes = mesh["nodes"]
chassis = next(i for i, n in enumerate(nodes) if n["name"] == "Chassis")
node = copy.deepcopy(nodes[chassis])
node["name"] = "Fan"
nodes.append(node)
if len(mesh["bind_matrices"]) == len(nodes) - 1:
    mesh["bind_matrices"].append(copy.deepcopy(mesh["bind_matrices"][chassis]))
next(p for p in mesh["parts"] if p["name"] == "Fan_LOD0")["placement_node"] = len(nodes) - 1

pack.replace_document(pack.model, mesh)
pack.save(sys.argv[sys.argv.index("--") + 1])
```

A `.blend` saved with a mesh in Edit Mode exports that mesh with empty UV data, which is why the
script leaves Edit Mode first. It never saves the `.blend`.

## Step 2 — a body in the `.hkx`

Clone a retail hinged body with JackAll's `HkxEdit`; the fan cloned `DoorLeftState02`. The clone keeps
the collision filter, quality type and motion type the game expects. Then change these members:

| Member | Fan value | Why |
|---|---|---|
| `name` | `Fan` | parts find their bodies by name |
| `motion.motionState.transform` | identity rotation at the chassis origin | this is the transform copied onto the node, and the node's rest is the chassis's |
| `…sweptTransform.centerOfMass0`, `…1` | the hub, in model space | the body turns about its centre of mass |
| `…sweptTransform.centerOfMassLocal` | the hub less the chassis origin | |
| `…sweptTransform.rotation0`, `…1` | `0 0 0 1` | |
| `motion.inertiaAndMassInv` | 1/4, 1/1.30, 1/4, 1/20 | the inverse inertia about the body's x, y and z, then the inverse mass |
| `motion.motionState.maxAngularVelocity` | byte 110, 65.6 rad/s | the doors' 86 is 13.3 rad/s, which would cap the fan near 2 Hz |
| `material.responseType` | 3, `RESPONSE_NONE` | Havok gives any pair with such a body no contact forces |
| `collidable.shape` | a 10 cm box in a translate shape on the hub | it touches nothing, but a body needs a shape |

The fan is a 20 kg disc of radius 0.36 m. That gives 0.5·m·r² = 1.30 kg·m² about its axis, which sets
the spin-up and coast torques. Across the axis it gets 4 kg·m², about a door's, so the hinge holds it
as firmly as it holds a door.

:::danger[Insert the body before the first `HIGH_` or `NAV_` body]
The game keeps a position, rotation and mass only for the bodies listed before the first one whose
name contains `HIGH_` or `NAV_`. It places a hinged part from that table without a bounds check, so a
body listed after it is built from whatever memory follows, different on every load. Everything else
finds bodies by name, so moving the later bodies down one is safe. See [`.hkx`](../file-formats/hkx.md#what-the-files-hold).
:::

## Step 3 — the hinge and its motor

Clone a retail `hkpLimitedHingeConstraintData` and its `hkpConstraintInstance`, then:

- **Frames**: `atoms.transforms.transformA` and `transformB` place the hinge in each body's space. The
  axis is each frame's first column and the pivot its translation. With both bodies' origins on the
  chassis origin, the two frames are the same.
- **Limits**: `atoms.angLimit.isEnabled` 0, so it turns freely.
- **Motor**: `atoms.angMotor.motor` points at a new `hkpVelocityConstraintMotor` (`type` 2), and
  `atoms.angMotor.isEnabled` is 1. The motor's `velocityTarget` is in rad/s, −2π·8 for the fan.
  `minForce` and `maxForce` are ± the torque that reaches that speed, and `tau` is 1.
- **Friction**: `atoms.angFriction.maxFrictionTorque` brakes the part once the motor is off. Havok
  applies the motor or the friction, never both, so `isEnabled` starts at 0 while the motor is on.
- **Instance**: `entities[0]` is the new body and `entities[1]` stays the chassis. Append the instance
  to the system's `constraints`.

**The forces are torques in N·m, and the result is plain physics.** The fan's motor gives
I·ω / 1 s = 65 N·m, and it reached 50.3 rad/s in 1.03 s in game. Its friction is I·ω / 10 s =
6.5 N·m. Together with the body's angular damping of 0.05 per second, that predicts a stop in 8.1 s,
and the fan stopped in about 8 s.

<details>
<summary>The script that builds the fan's `.hkx`</summary>

`mods/vehicle-overhaul/assets/physics/fan.cs`, gitignored; the layer carries the `.hkx` it writes.
Run it with `dotnet run fan.cs`.

```csharp
#:project ../../../../tools/JackAll/src/JackAll.Tools/JackAll.Tools.csproj
using JackAll.Tools.Hkx;

string root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? ".", "..", "..", "..", ".."));
string retail = Path.Combine(root, @"tmp\gamefiles\worlds\worlds\graphics\vehicles\land\bigtruck\bigtruck.hkx");
string layer = Path.Combine(root, @"mods\vehicle-overhaul\layer\mods\graphics\vehicles\land\bigtruck\bigtruck.hkx");

float[] hub = [-0.024936f, 3.27019f, 1.29549f];
const float Hz = 8, SpinUpSeconds = 1, CoastSeconds = 10;
const float Mass = 20, Radius = 0.36f, AcrossInertia = 4;
const byte NoResponse = 3;
const byte MaxAngularVelocity = 110;

HkxFile file = HkxFile.Parse(File.ReadAllBytes(retail));
List<HkxObject> objects = file.Objects();
HkxObject Named(string className, string name) => objects.Single(o => o.Class.Name == className && o.GetString("name") == name);

HkxObject system = objects.Single(o => o.Class.Name == "hkpPhysicsSystem");
HkxObject chassis = Named("hkpRigidBody", "Chassis");
HkxObject door = Named("hkpRigidBody", "DoorLeftState02");
HkxObject doorHinge = Named("hkpConstraintInstance", "DoorLeftState02");

float[] origin = chassis.GetReals("motion.motionState.transform")[12..15];
float[] pivot = [hub[0] - origin[0], hub[1] - origin[1], hub[2] - origin[2]];
float spin = 2 * MathF.PI * Hz;
float axial = 0.5f * Mass * Radius * Radius;

HkxObject translate = objects.First(o => o.Class.Name == "hkpConvexTranslateShape" && o.GetPointer("childShape.childShape")?.Class.Name == "hkpBoxShape").Clone();
HkxObject box = translate.GetPointer("childShape.childShape")!.Clone();
box.SetReals("radius", 0.01f);
box.SetReals("halfExtents", 0.05f, 0.05f, 0.05f, 0.01f);
translate.SetReals("radius", 0.01f);
translate.SetPointer("childShape.childShape", box);
translate.SetReals("translation", pivot[0], pivot[1], pivot[2], 0);

HkxObject fan = door.Clone();
fan.SetString("name", "Fan");
fan.SetPointer("collidable.shape", translate);
fan.SetByte("material.responseType", NoResponse);
fan.SetReals("motion.motionState.transform", 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, origin[0], origin[1], origin[2], 1);
fan.SetReals("motion.motionState.sweptTransform.centerOfMass0", hub[0], hub[1], hub[2], 0);
fan.SetReals("motion.motionState.sweptTransform.centerOfMass1", hub[0], hub[1], hub[2], 0);
fan.SetReals("motion.motionState.sweptTransform.rotation0", 0, 0, 0, 1);
fan.SetReals("motion.motionState.sweptTransform.rotation1", 0, 0, 0, 1);
fan.SetReals("motion.motionState.sweptTransform.centerOfMassLocal", pivot[0], pivot[1], pivot[2], 0);
fan.SetReals("motion.motionState.objectRadius", 0.1f);
fan.SetByte("motion.motionState.maxAngularVelocity", MaxAngularVelocity);
fan.SetReals("motion.inertiaAndMassInv", 1 / AcrossInertia, 1 / axial, 1 / AcrossInertia, 1 / Mass);

HkxObject motor = HkxEdit.Create("hkpVelocityConstraintMotor");
motor.SetByte("type", 2);
motor.SetReals("minForce", -axial * spin / SpinUpSeconds);
motor.SetReals("maxForce", axial * spin / SpinUpSeconds);
motor.SetReals("tau", 1);
motor.SetReals("velocityTarget", -spin);

float[] frame = [0, 1, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, pivot[0], pivot[1], pivot[2], 0];
HkxObject hinge = doorHinge.GetPointer("data")!.Clone();
hinge.SetReals("atoms.transforms.transformA", frame);
hinge.SetReals("atoms.transforms.transformB", frame);
hinge.SetByte("atoms.angMotor.isEnabled", 1);
hinge.SetReals("atoms.angMotor.targetAngle", 0);
hinge.SetPointer("atoms.angMotor.motor", motor);
hinge.SetByte("atoms.angFriction.isEnabled", 0);
hinge.SetReals("atoms.angFriction.maxFrictionTorque", axial * spin / CoastSeconds);
hinge.SetByte("atoms.angLimit.isEnabled", 0);

HkxObject constraint = doorHinge.Clone();
constraint.SetString("name", "Fan");
constraint.SetPointer("data", hinge);
constraint.SetPointer("entities[0]", fan);

List<HkxObject?> bodies = system.GetPointers("rigidBodies");
int firstHighOrNav = bodies.FindIndex(b => b!.GetString("name") is { } n && (n.Contains("HIGH_") || n.Contains("NAV_")));
system.Insert("rigidBodies", firstHighOrNav < 0 ? bodies.Count : firstHighOrNav, fan);
system.Append("constraints", constraint);

File.WriteAllBytes(layer, file.Write());
```

</details>

## Step 4 — the part in the entity

The entity needs two things beside the model, both in its archetype in the entity library:

- **A graphic object per state that draws**, in `CGraphicComponent`. Each names the mesh
  (`hidMeshName` `Fan`), the node (`hidNodeName`, the CRC-32 of `Fan`) and the LOD0 part
  (`hidNodeNameLOD0`, the CRC-32 of `Fan_LOD0`). The fan has objects 45 and 46, one per state.
- **A `Part`** in `VehicleWheeledPhysComponent`'s parts list:
  - `Name` is the CRC-32 of `Fan`.
  - `fHealth` is 5000.
  - `bKeepAttached` is False.
  - All stims are off, and `Sounds` is empty.
  - Each state has one `hidState` and one `hidStateFX`. A state names its body (`hidRigidbodyName`
    and `hidHighresRigidbodyName`), its graphic object and a part id.

The fan has two states:

- **State 0 is still.** Its body, `FanState01`, is in no `.hkx`.
- **State 1 is hinged.** Its body is `Fan`.

A state is hinged only when a constraint names its body. Hashes are exact-case CRC-32: `Fan` is
`5DBA3ED9` and `Fan_LOD0` is `3764F2B7`.

The fan also carries a matching `<object>`, `<bone>` and `<accessory>` in the descriptor, to keep it
consistent with the rest. The retail game reads none of them.

## Step 5 — the plugin

Without a plugin, the game hinges a part at spawn when the body of its starting state is constrained,
and the motor then runs forever on every truck. The fan's plugin is `src/fan.cpp`, with the reusable
parts in `src/engine/parts.cpp` and `src/engine/hinge.cpp`. It does three things from a hook on
`CVehiclePhysComponent::OnMove` (Steam `0x1006EE10`), which runs every frame for every vehicle:

1. **It hinges the part when the player's engine starts.** `CPart::GotoStateIndex` (Steam
   `0x1006D920`) moves the part from state 0 to state 1 quietly. It builds the hinge because
   `bKeepAttached` is false. States only move forward, so the part stays hinged.
2. **It switches the motor with the engine.** The component's `+0x100` is the engine-running flag.
   The switch writes what Havok's `setMotorActive` writes into the hinge data: `+0xA2` enables the
   motor and `+0xB6` the friction, always the opposite of each other. Havok's call also resets the
   motor's runtime state. The plugin skips that step, and the fan works without it.
3. **It saves the part in state 0.** A hook on `GetSavedParts` (Steam `0x1006E120`) does this, so a
   loaded save starts the fan still rather than hinged.

To find what it needs, the plugin follows these pointers:

- **The car**: the frame part's body is a `CPhysEntity`, and its vtable slot `0x28` returns the
  vehicle entity. For a car that is a `CPhysWheeledVehicleEntity` wrapper. The
  `CPhysWheeledVehicleEntityImpl` that [vehicle physics](../engine-internals/vehicle-physics.md#layout)
  describes is at the wrapper's `+0x5C`.
- **The hinge**: start from the chassis `hkpRigidBody` and scan both its master and its slave
  constraint lists. The fan's hinge was in the master list. Recognise the hinge by its data: an
  angular motor atom at `+0xA0` whose motor is a velocity motor.

The plugin keeps one motor state for all fans; whether two trucks of one model share their hinge data
was not checked. The fan only ever spins for the player, so trucks with AI drivers keep it still.

## Measuring it

Read a body's state from Havok rather than judging it by eye. A Havok object's runtime layout is its
packfile layout, so `HavokClasses.Default.Slots(c)` in JackAll gives any member's offset. These were
the ones used:

| Class | Member | Offset |
|---|---|---|
| `hkpRigidBody` | `motion.linearVelocity` | `+0x1A0` |
| `hkpRigidBody` | `motion.angularVelocity`, in world space | `+0x1B0` |
| `hkpRigidBody` | `motion.type` | `+0xD8` |
| `hkpRigidBody` | `simulationIsland` | `+0xB8` |
| `hkpConstraintInstance` | `data` | `+0x0C` |
| `hkpConstraintInstance` | `entities[0]`, `entities[1]` | `+0x14`, `+0x18` |

The spin-up and coast times above come from logging the fan's angular velocity, less the chassis's,
every 100 ms for 8 s after each motor switch. Pace a log like this by time rather than by frame count.
