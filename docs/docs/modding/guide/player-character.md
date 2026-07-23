---
sidebar_position: 10
sidebar_label: "Player Character"
format: md
---

:::info[From the Almost Complete Guide]
This page reproduces a section of ["An Almost Complete Guide to Far Cry 2 Modding"](./index.md) by Boggalog, verbatim, with attribution. See the [guide index](./index.md) for the full attribution note.
:::

# Player character

## Damage dealt to enemies

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Damage dealt to enemies is controlled by the “\<HitLocations\>” section of this file.

There are three sections with this title, which control singleplayer damage, multiplayer damage and multiplayer hardcore damage. The singleplayer section has the line “\<\!-- Lists the damage multipliers for various hit locations \--\>” directly before it.

Damage multipliers for each body part are listed. These work as proportional modifiers, so any value lower than 1 will decrease player damage and a value of 0.5 will decrease player damage by 50%.

```xml {3,4,5,6,7,8}
<!-- Lists the damage multipliers for various hit locations -->
<HitLocations>
<Head multiplier="6.0"/>
<Torso multiplier="1.0"/>
<Arms multiplier="1.0"/>
<Legs multiplier="0.5"/>
<Hands multiplier="0.5"/>
<Feet multiplier="0.5"/>
</HitLocations>
```

### 

## Fall damage

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Fall damage is controlled by the “\<JumpDamage” stats under the “\<DefaultCountersService\>” title.

There are four stats here, which control two different aspects of fall damage.

“fMinSpeedFallDamage” controls the minimum speed that the player has to fall to inflict fall damage, and “fMaxSpeedFallDamage” controls the speed at which the play has to fall to inflict maximum fall damage.

“iMinFallLevelStim” controls the minimum damage that can be inflicted from a single fall, and “iMaxFallLevelStem” controls the maximum damage that can be inflicted from a single fall. 

To remove fall damage completely set both “fMinSpeedFallDamage” and “fMaxSpeedFallDamage” to 2000\.

```xml {2}
<DefaultCountersService>
        <JumpDamage fMinSpeedFallDamage="14" fMaxSpeedFallDamage="17" iMinFallLevelStim="2" iMaxFallLevelStim="32" />
```

### 

## Health/Healing

Player health is controlled by two stats, overall max health and the size of each individual health bar. **If you want to increase/decrease player health you need to change both of these by the same proportion e.g. both x2.**

### Max health

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

There are separate sections controlling max health for each difficulty, the titles of these are: 

PlayerSicknessCurves.HealthMax\_Casual  
PlayerSicknessCurves.HealthMax\_Experienced  
PlayerSicknessCurves.HealthMax\_Hardcore  
PlayerSicknessCurves.HealthMax\_Infamous

There are two stats for each difficulty, I don’t know how they interact but you need to change both by the same proportion. 

The sections look like this, with the stats you need to change highlighted in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.HealthMax_Casual</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.HealthMax_Casual</value>
      <value name="disEntityId" type="UInt64">168</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>1800</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>1300</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

### 

### Health bar size

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

There are separate sections controlling health bar size for each difficulty, the titles of these are: 

PlayerSicknessCurves.HealthBarSize\_Casual  
PlayerSicknessCurves.HealthBarSize\_Experienced  
PlayerSicknessCurves.HealthBarSize\_Hardcore  
PlayerSicknessCurves.HealthBarSize\_Infamous

There are two stats for each difficulty, they need to be changed by the same proportion that you used to change the max health values. The two values for health bar size need to be equal to the max health values divided by 5, as the game’s ui relies on the player having 5 health bars in total.

The sections look like this, with the stats you need to change highlighted in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.HealthBarSize_Casual</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.HealthBarSize_Casual</value>
      <value name="disEntityId" type="UInt64">162</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>360</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>260</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

### Bug fix \- Restoring critical healing animations to Infamous difficulty

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

There are two sections in this file with the title “HealthDegenerationLevels”, one for singleplayer and one for multiplayer. We are going to edit the singleplayer section, which looks like this:

```xml
<HealthDegenerationLevels>
	<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.EasyDegenerationRate" />
	<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.CasualDegenerationRate" />
<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.HardcoreDegenerationRate" />
<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.InfamousHealTime" />
</HealthDegenerationLevels>
```

To restore the correct critical healing animations, we are going to replace the “Curves.PlayerSicknessCurves.InfamousHealTime” section with “Curves.PlayerSicknessCurves.InfamousDegenerationRate”. Your finished section should look like this:

```xml {5}
<HealthDegenerationLevels>
	<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.EasyDegenerationRate" />
	<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.CasualDegenerationRate" />
<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.HardcoreDegenerationRate" />
<DifficultyLevel CurveName="Curves.PlayerSicknessCurves.InfamousDegenerationRate" />
</HealthDegenerationLevels>
```

### Guide \- Enhanced First Aid

This guide will cover enabling “Enhanced First Aid”, which means that when the player has no syrettes left they can hold the heal button and perform a full heal with a critical healing animation.

Step 1: Adding a new healing script  
main\_avater.gosm.xml (\\patch\_unpack\\scripts\\engine\\objects\\pawn\\statemachine\\)

Find the section of this file with this title:

```xml
<State FullName="::Main Avatar/Common/Idle" Type="CGOStateAnim">
```

Add these lines at the bottom of this section:

```xml
<Sink Name="enhanced first aid" Start="0" End="100">
	<Connection Target="::Healing/Healing/States/Entering Healing" Signal="enfirstaid" />
</Sink>
```

Your finished section should look like this:

```xml
<State FullName="::Main Avatar/Common/Idle" Type="CGOStateAnim">
	<Parameter Name="groups">
		<Parameter Name="0" Value="Idle" />
		<Parameter Name="1" Value="can_ironsight" />
	</Parameter>
	<Parameter Name="duration" Value="0" />
	<Parameter Name="signalpriorities" />
	<Parameter Name="forceAnim" Value="0" />
	<Parameter Name="syncAnimDuration" Value="0" />
	<Parameter Name="animStateID" Value="Pawn_Generic_Movement" />
	<Parameter Name="layerStateID" Value="Pawn_Generic_Aim" />
	<Parameter Name="gestureStateID" Value="0" />
	<Parameter Name="followTerrain" Value="0" />
	<Parameter Name="MoveLayer" Value="-1" />
	<Sink Name="Slide" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/Slide/StartSliding" Signal="start_sliding" />
	</Sink>
	<Sink Name="random idle" Start="0" End="100">
		<Connection Target="::Main Avatar/Common/IdleCycleBreaker" Signal="cyclebreaker" />
	</Sink>
<Sink Name="enhanced first aid" Start="0" End="100">
		<Connection Target="::Healing/Healing/States/Entering Healing" Signal="enfirstaid" />
	</Sink>	
</State>
```

Step 2: Adding the key binding  
inputactionmapcommon.xml (\\patch\_unpack\\config\\)

Find the section of this file with this title:

```xml
<ActionMap name="common_gameplay">
```

We are going to add a line each to the keyboard and gamepad sections that says if we hold their respective heal buttons it will execute our healing script. These lines are:

```xml
<!--Keyboard-->
<Binding input="kb:h" action="hold" signal="enfirstaid"/>
```

```xml
<!--Gamepad-->
<Binding input="pad:left_shoulder" action="hold" signal="enfirstaid"/>
```

Your completed section should look like this:

```xml
<ActionMap name="common_gameplay">
    <Import actionmap="common_gameplay_remap" optional=""/>
    <import actionmap="common_use_remap" optional=""/>
    <import actionmap="common_heal_remap" optional=""/>
    <import actionmap="common_jump_remap" optional=""/>
    <import actionmap="common_crouch_remap" optional=""/>
    <import actionmap="common_grenade_remap" optional=""/>
```
    		  
```xml {6}
	<!--Keyboard-->
	<Binding input="kb:space" action="press" signal="jump"/>
	<Binding input="kb:c" action="press" signal="crouch"/>
	<Binding input="kb:e" action="press" signal="use"/>
	<Binding input="kb:h" action="press" signal="heal"/>
	<Binding input="kb:h" action="hold" signal="enfirstaid"/>
	<Binding input="kb:q" action="press" signal="throw_grenade"/>
	<Binding input="kb:f" action="press" signal="select_next_throw_gadget"/>
```
		  
```xml
	<!--Demo keys-->		
	<Binding input="kb:^" action="press" signal="start_rain_demo" />
	<Binding input="kb:&amp;" action="press" signal="stop_rain_demo" />
	<Binding input="kb:*" action="press" signal="start_time_demo" />
	<Binding input="kb:(" action="press" signal="start_storm_demo" />
	<Binding input="kb:)" action="press" signal="stop_storm_demo" />
```

		  
```xml {6}
	<!--Gamepad-->
	<Binding input="pad:a" action="press" signal="jump"/>
	<Binding input="pad:y" action="press" signal="use"/>
	<Binding input="pad:b" action="press" signal="crouch"/>
	<Binding input="pad:left_shoulder" action="press" signal="heal"/>
	<Binding input="pad:left_shoulder" action="hold" signal="enfirstaid"/>
	<Binding input="pad:right_shoulder" action="press" signal="throw_grenade"/>
	<Binding input="pad:right_thumb_push" action="press" signal="select_next_throw_gadget"/>
</ActionMap>
```

## Jump height

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)  
Starting a new game is required for changes to this stat to take effect

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Jump height is controlled by two stats, “fJumpHeight” and “fJumpHeightExhausted”, both in the “Body” section.

“fJumpHeight” controls regular jump height and jump height when out of stamina is controlled by “fJumpHeightExhausted”.

These work as proportional modifiers, so any value lower than 1 will decrease jump height and a value of 0.5 will decrease jump height by 50%.

```xml {2,3}
<object type="Body">
       <value name="fJumpHeight" type="Float">1</value>
       <value name="fJumpHeightExhausted" type="Float">0.4</value>
       <value name="fGravity" type="Float">-18</value>
```

## Malaria

### Time between malaria attacks

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The time between malaria attacks is controlled by the “PlayerSicknessCurves.MalariaTimeBetweenEachAttack” section.

This section is controlled with two identical values that make up a timer. Increase the value to reduce malaria attacks and decrease the value to increase malaria attacks.

The section looks like this, with the relevant stats in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.MalariaTimeBetweenEachAttack</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.MalariaTimeBetweenEachAttack</value>
      <value name="disEntityId" type="UInt64">179</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

### Removing malaria

gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

To remove malaria completely find the “\<malaria” section of the file, it looks like this:

```xml
<Malaria
```
FirstAttackTime="Curves.PlayerSicknessCurves.MalariaTimeBeforeFirstAttack"  
BetweenAttackTime="Curves.PlayerSicknessCurves.MalariaTimeBetweenEachAttack"  
MinorAttackQte="Curves.PlayerSicknessCurves.MalariaMaxNumberOfMinorAttack"  
MinorAttackDuration="Curves.PlayerSicknessCurves.MalariaMinorAttackDuration"  
/\>

Change it to this:

```xml
<Malaria
```
FirstAttackTime \= "**Curves.PlayerSicknessCurves.HealthMax\_Casual**"  
     	BetweenAttackTime \= "**Curves.PlayerSicknessCurves.HealthMax\_Casual**"  
MinorAttackQte \= "**Curves.PlayerSicknessCurves.HealthMax\_Casual**"  
MinorAttackDuration \= "**Curves.PlayerSicknessCurves.HealthMax\_Casual**"  
/\>

### 

## Movement speed

### Walking/Crouch walking

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Walk speed is controlled by the “fWalkingMaxSpeed” stat in the “Body” section. The max value for this is “5” as any higher will cause the sprint animation when walking.

Crouch walking speed is controlled by the “fWalkingMaxSpeedCrouch” stat in the “Body” section.

```xml {5,6}
<object type="Body">
       <value name="fJumpHeight" type="Float">1</value>
       <value name="fJumpHeightExhausted" type="Float">0.4</value>
       <value name="fGravity" type="Float">-18</value>
       <value name="fWalkingMaxSpeed" type="Float">3.8</value>
       <value name="fWalkingMaxSpeedCrouch" type="Float">2.5</value>
```

### Sprinting

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Sprint speed is controlled by the “Locomotion.Sprint” section of this file.

There are a significant number of values that control sprint speed. I don’t know how they all work but from what I could interpret there is a series of values that control acceleration, and one value that controls max speed.

In my experience I was able to increase sprint acceleration by 2x and max sprint speed by 3x while maintaining the sprint animation, any higher and the arms disappeared while sprinting.

This is the sprint section with sprint acceleration values highlighted **red** and the max sprint speed value highlighted **blue**:

```xml {28,29,43,44,58,59,73,74,88,89,103,104,118,119,133,134,148,149,163,164,178,179,193,194,208,209,223,224,238,239}
<object hash="256A1FF9">
    <value name="Name" type="String">Locomotion.Sprint</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.Locomotion.Sprint</value>
      <value name="disEntityId" type="UInt64">153</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">16</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>-0.0056</x>
              <y>4.9611</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.5054</x>
              <y>8.8956</y>
              <z>2.2014</z>
              <w>-0.0950714</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.5054</x>
              <y>8.8956</y>
              <z>2.2014</z>
              <w>-0.0950714</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.6035</x>
              <y>8.9839</y>
              <z>2.2014</z>
              <w>-0.0950714</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.6035</x>
              <y>8.9839</y>
              <z>1.5298</z>
              <w>1.29931</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.8081</x>
              <y>8.8846</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.8081</x>
              <y>8.8846</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.8992</x>
              <y>8.6577</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0.8992</x>
              <y>8.6577</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.2946</x>
              <y>6.6723</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.2946</x>
              <y>6.6723</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.3738</x>
              <y>6.4194</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.3738</x>
              <y>6.4194</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.5322</x>
              <y>6.2996</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>1.5322</x>
              <y>6.2996</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>6.1281</x>
              <y>6.0708</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

### Sprinting turn modifier

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

The sprint turn modifier is controlled by the stat “fSprintingTurnModifier" in the “Body” section. This modifier controls how much you slow down when turning while sprinting. 

This works as a proportional modifier, so any value lower than 1 will decrease sprint speed when turning and a value of 0.5 will decrease sprint speed when turning by 50%.

```xml {1}
<value name="fSprintingTurnModifier" type="Float">0.2</value>
```

### Swimming

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Swimming speed is controlled by two stats, "fSwimmingMaxSpeed" and "fSwimmingAcceleration", both in the “Body” section. The purposes of these should be obvious.

```xml {1,2}
<value name="fSwimmingMaxSpeed" type="Float">5</value>
<value name="fSwimmingAcceleration" type="Float">5</value>
```

### Swimming underwater

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Swimming speed when underwater is controlled by two stats, "fDivingMaxSpeed" and "fDivingAcceleration", both in the “Body” section. The purposes of these should be obvious.

```xml {1,2}
<value name="fDivingMaxSpeed" type="Float">5</value>
<value name="fDivingAcceleration" type="Float">5</value>
```

## Slope climbing ability

xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Each playable character has their own section of this file, these are the entry titles:

MainCharacter.PawnPlayer.Andre\_Hyppolite  
MainCharacter.PawnPlayer.Frank\_Bilders  
MainCharacter.PawnPlayer.Hakim\_Echebbi  
MainCharacter.PawnPlayer.Josip\_Idromeno  
MainCharacter.PawnPlayer.Marty\_Alencar  
MainCharacter.PawnPlayer.Paul\_Ferenc  
MainCharacter.PawnPlayer.Quarbani\_Singh  
MainCharacter.PawnPlayer.Warren\_Clyde  
MainCharacter.PawnPlayer.Xianyong\_Bai

Slope climbing ability is controlled by two stats, “fMaxSlope” and “fMaxTerrainSlope”, both in the “CharacterParams” section.

These two stats control the player’s ability to climb two different kinds of slope. “fMaxTerrainSlope” controls the ability to climb the mountains and hills that mark the edge of the playable areas. “fMaxSlope” controls the ability to climb everything else, including the roofs of buildings and large boulders found within the playable areas. Increasing “fMaxSlope” too much can make it impossible to fall through small gaps, such as trying to fall through the North Railyard roof to reach the diamond briefcase.

```xml {5,6}
<object type="CharacterParams">
       <value name="fMass" type="Float">80</value>
       <value name="bUpdateRotation" type="Bool">False</value>
       <value name="bUseRigidBased" type="Bool">False</value>
       <value name="fMaxSlope" type="Float">60</value>
       <value name="fMaxTerrainSlope" type="Float">45</value>
```

## Stamina

### Sprint stamina drain

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Sprint stamina drain is controlled by the “PlayerSicknessCurves.StaminaSprintDrain” section of this file.

There are two stats to change, with the same change applied to both. The default stat for sprint stamina drain is \-10. This is relative to 0, which would mean there is no sprint stamina drain.

The section looks like this, with the stats you need to change highlighted in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.StaminaSprintDrain</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.StaminaSprintDrain</value>
      <value name="disEntityId" type="UInt64">187</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>-10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>-10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

### Jump stamina drain

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

Sprint stamina drain is controlled by the “PlayerSicknessCurves.StaminaJumpDrain” section of this file.

There are two stats to change, with the same change applied to both. The default stat for jump stamina drain is 10\. This is relative to 0, which would mean there is no jump stamina drain.

The section looks like this, with the stats you need to change highlighted in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.StaminaJumpDrain</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.StaminaJumpDrain</value>
      <value name="disEntityId" type="UInt64">182</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>10</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

## Underwater breath

xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The time the player can remain underwater is controlled by the “PlayerSicknessCurves.HealthDrownRate” section of this file.

This section is controlled with two identical values that control the rate at which the player loses health when drowning. The default rate of health loss is \-50, which is relative to 0 which would mean the player can stay underwater forever.

The section looks like this, with the relevant stats in red:

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.HealthDrownRate</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.HealthDrownRate</value>
      <value name="disEntityId" type="UInt64">166</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>-50</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>-50</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

## Guide \- Desert exploration

This guide will cover how we can control the extent to which desert exploration is possible. The game works with stamina being drained when in the desert and once you have no stamina left you collapse. We are going to edit how fast the desert drains stamina but it is possible to run off the edges of the map so it’s up to you how much freedom you allow your players.

Step 1: Possible simple solution  
gamemodesconfig.xml (\\patch\_unpack\\engine\\gamemodes\\)

Find the stamina section of this file by searching for “\<Malaria”. The singleplayer stamina section is directly above this and looks like this:

```xml
<Stamina
```
Max="Curves.PlayerSicknessCurves.StaminaMax"  
ActionThreshold="Curves.PlayerSicknessCurves.StaminaSprintThreshold"  
NearZeroFX="Curves.PlayerSicknessCurves.StaminaNearZeroFX"  
RegenRate="Curves.PlayerSicknessCurves.StaminaRegenRate"  
SprintDrain="Curves.PlayerSicknessCurves.StaminaSprintDrain"  
LowDrain="Curves.PlayerSicknessCurves.StaminaLowDrain"  
HighDrain="Curves.PlayerSicknessCurves.StaminaHighDrain"  
SwimDrain="Curves.PlayerSicknessCurves.StaminaSprintDrain"  
DrownHealthDrain="Curves.PlayerSicknessCurves.HealthDrownRate"  
JumpDrain="Curves.PlayerSicknessCurves.StaminaJumpDrain"  
/\>

This section lists different actions and the relevant curves linked to them.

The “LowDrain” and “HighDrain” curves control desert stamina drain. “LowDrain” controls drain at the edge of the desert and “HighDrain” controls drain further in. “LowDrain” already allows some exploration with an initial drain value of \-0.5 which increases to \-4 over time, “HighDrain” is designed to stop you in your tracks with a flat drain value of \-20.

An easy simple solution which allows some desert exploration is to swap the “HighDrain” curve to the “LowDrain” curve, so "Curves.PlayerSicknessCurves.StaminaLowDrain".

With this solution desert exploration is still pretty limited, if you want more then we need to make a new curve.

Step 2: Creating a new stamina drain curve  
xx\_Curves.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

To create a new curve you can use the section below as a base. This section has the title “Curves.PlayerSicknessCurves.StaminaDesertDrain” and this can be pasted into the “LowDrain” and “HighDrain” curve values in the stamina section within “gamemodesconfig.xml” as mentioned in the previous step.

There are two values to change in this section, which are highlighted red. Change both values to the same number and they must be negative to drain stamina. I suggest choosing a value between \-1 and \-4.

You can could change the value to 0 so you can explore the desert unhindered, but if you run out of stamina through sprinting or jumping then you will still collapse.

```xml {14,29}
<object hash="256A1FF9">
    <value name="Name" type="String">PlayerSicknessCurves.StaminaDesertDrain</value>
    <object type="Entity">
      <value name="hidName" type="String">Curves.PlayerSicknessCurves.StaminaDesertDrain</value>
      <value name="disEntityId" type="UInt64">181</value>
      <value hash="D2B3429E" type="String">CCurve</value>
      <value name="hidEntityClass" type="Hash">68745CCF</value>
      <object type="curveCurve">
        <value name="hidNumKnots" type="UInt32">2</value>
        <object type="Knots">
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>0</x>
              <y>-2</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
          <object type="Knot">
            <value name="Value" type="Vector4">
              <x>5</x>
              <y>-2</y>
              <z>0.5</z>
              <w>0</w>
            </value>
            <value name="Info" type="Vector4">
              <x>6.2832</x>
              <y>1</y>
              <z>0</z>
              <w>0</w>
            </value>
            <value name="Type" type="UInt32">0</value>
          </object>
        </object>
      </object>
    </object>
  </object>
```

The completed stamina section from “gamemodesconfig.xml” should look like this:

```xml
<Stamina
```
Max="Curves.PlayerSicknessCurves.StaminaMax"  
ActionThreshold="Curves.PlayerSicknessCurves.StaminaSprintThreshold"  
NearZeroFX="Curves.PlayerSicknessCurves.StaminaNearZeroFX"  
RegenRate="Curves.PlayerSicknessCurves.StaminaRegenRate"  
SprintDrain="Curves.PlayerSicknessCurves.StaminaSprintDrain"  
LowDrain="**Curves.PlayerSicknessCurves.StaminaDesertDrain**"  
HighDrain="**Curves.PlayerSicknessCurves.StaminaDesertDrain**"  
SwimDrain="Curves.PlayerSicknessCurves.StaminaSprintDrain"  
DrownHealthDrain="Curves.PlayerSicknessCurves.HealthDrownRate"  
JumpDrain="Curves.PlayerSicknessCurves.StaminaJumpDrain"  
/\>

## 

## 

## 

## 

## 

## Guide \- Adding the female mercenaries as playable characters

This guide will cover how to add the female mercenaries as playable characters. These new characters won’t replace any of the existing options. A similar method could be used for making any character in the game playable.

Step 1: Adding new entries to the character select menu  
sp\_avatar.mgb.desc (\\patch\_unpack\\ui\\)  
There are different folders for widescreen/non-widescreen aspect ratios and the different languages, it’s pretty self-explanatory when you see it.

In this file you will see the list of available characters under the title \<avatar\_list\>. Add the following lines to the end of this section. These MUST be add at the end, it is not possible to add them midway through the existing options:

```xml
<avatar buddyName="Michele_Dachss" displayName="Michele Dachss" text="STORYMODE_AVATAR_MICHELE" />
<avatar buddyName="Flora_Guillen" displayName="Flora Guillen" text="STORYMODE_AVATAR_FLORA" />
<avatar buddyName="Nasreen_Davar" displayName="Nasreen Davar" text="STORYMODE_AVATAR_NASREEN" />
```

Your completed section should look like this:

```xml {11,12,13}
<avatar_list>
	<avatar buddyName="Marty_Alencar" displayName="Marty Alencar" text="STORYMODE_AVATAR_MARTY" />
	<avatar buddyName="Warren_Clyde" displayName="Warren Clyde" text="STORYMODE_AVATAR_WARREN" />
	<avatar buddyName="Josip_Idromeno" displayName="Josip Idromeno" text="STORYMODE_AVATAR_JOSIP" />
	<avatar buddyName="Paul_Ferenc" displayName="Paul Ferenc" text="STORYMODE_AVATAR_PAUL" />
	<avatar buddyName="Quarbani_Singh" displayName="Quarbani Singh" text="STORYMODE_AVATAR_QUARBANI" />
	<avatar buddyName="Andre_Hyppolite" displayName="Andre Hyppolite" text="STORYMODE_AVATAR_ANDRE" />
	<avatar buddyName="Hakim_Echebbi" displayName="Hakim Echebbi" text="STORYMODE_AVATAR_HAKIM" />
	<avatar buddyName="Frank_Bilders" displayName="Frank Bilders" text="STORYMODE_AVATAR_FRANK" />
	<avatar buddyName="Xianyong_Bai" displayName="Xianyong Bai" text="STORYMODE_AVATAR_XIANYONG" />
	<avatar buddyName="Nasreen_Davar" displayName="Nasreen Davar" text="STORYMODE_AVATAR_NASREEN" />
	<avatar buddyName="Michele_Dachss" displayName="Michele Dachss" text="STORYMODE_AVATAR_MICHELE" />
	<avatar buddyName="Flora_Guillen" displayName="Flora Guillen" text="STORYMODE_AVATAR_FLORA" />
</avatar_list>
```

Step 2: Adding character menu info text  
\\patch\_pack\\languages\\ \- Each language has its own folder and “oasisstrings.rml” file

Within this file search for “STORYMODE\_AVATAR\_XIANYONG” and you will be taken to the correct section.

We are going to add three new entries. There is a particular structure as the single line contains multiple menu lines of text, so copy another player character's lines and paste them below the existing ones three times. These new entries should be titled like this:

```xml
<string enum="STORYMODE_AVATAR_NASREEN" …
<string enum="STORYMODE_AVATAR_MICHELE" ...
<string enum="STORYMODE_AVATAR_FLORA" ...
```

After the titles you will see there are headings for different character details like age, place of birth, height etc. You can edit these to your liking\!

(Optional) Step 3: Creating character menu images  
avatar\_bai.xbt (\\patch\_unpack\\common\_unpack\\ui\\textures\\avatars\\)

At this point it is worth noting that the character menu doesn’t fully allow new entries to be added. As mentioned in step 1 our new characters must be added to the end of the list and this is because otherwise all the character photos are pushed out of order. Because our new characters are at the end of the list they also share the photo for Xianyong Bai. So, if you want to create new images for the female characters you can only edit the “avatar\_bai.xbt” file and use an identical image for Xianyong and any you have added.

Step 4: Editing character models to use for playable characters  
\\worlds\_unpack\\graphics\\actors\\

To use an existing model as a playable character model we are going to create a new version that doesn’t have any head/facial features. Otherwise the camera is clipped inside the head and you’re looking at the back side of their face.

All of these model files can be found by unpack “worlds.fat/.dat” and then within \\worlds\_unpack\\graphics\\actors\\. You will see every character has a folder and each contains a .xbg file. The female character files are located here:

\\worlds\_unpack\\graphics\\actors\\buddy\_floraguillen\\**floraguillen.xbg**  
\\worlds\_unpack\\graphics\\actors\\buddy\_micheledachss\\**micheledachss.xbg**  
\\worlds\_unpack\\graphics\\actors\\buddy\_nasreendavar\\**nasreendavar.xbg**

Our first step here is to copy these files into our patch folder with the same folder structure. You also need to rename them. I suggest keeping the same naming structure as the existing playable character models, so in the end your .xbg files are located and named like this:

\\patch\_unpack\\graphics\\actors\\buddy\_floraguillen\\**floraguillen\_avatar.xbg**  
\\patch\_unpack\\graphics\\actors\\buddy\_micheledachss\\**micheledachss\_avatar.xbg**  
\\patch\_unpack\\graphics\\actors\\buddy\_nasreendavar\\**nasreendavar\_avatar.xbg**

Once you have your files located and named correctly, open your chosen.xbg file in a hex editor and it will look like this:

You can see that in the right hand column, at the start of the file there are a series of .xbm files listed that look like this:

GRAPHICS\\\_MATERIALS\\FCHAPPART-M-2008031738101486.xbm

To remove the head/facial features we are going to remove some of these files by changing the file extension to ‘x.m’. To do this replace the ‘b’ hex byte to 00 in the left hand column. It should look like the example below where I have highlighted the changed hex byte:

You need to make this change to several different .xbm files per character, for the female characters they are:

Nasreen  
FCHAPPART-M-2008031738101486.xbm  
FCHAPPART-M-2008022137135450.xbm  
YCLOUTIER-M-2008072836989339.xbm  
YCLOUTIER-M-2008072062938116.xbm

Michele  
LJSIMPSON-M-2008050638796904.xbm  
FCHAPPART-M-2008060356325424.xbm  
FCHAPPART-M-2008022137135450.xbm  
FCHAPPART-M-2008031738101486.xbm  
LJSIMPSON-M-2008050236868286.xbm  
LJSIMPSON-M-2008050560164723.xbm

Flora  
FCHAPPART-M-2008022137135450.xbm  
FCHAPPART-M-2008031738101486.xbm  
LJSIMPSON-M-2008051557329970.xbm  
YCLOUTIER-M-2008052156569844.xbm

Step 5: Connecting player character entries to models  
xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

This file lists all the possible player characters. The female mercenaries already have entries, presumably because at one point they were planned to be included. These are their titles:

MainCharacter.PawnPlayer.Nasreen\_Davar  
MainCharacter.PawnPlayer.Michele\_Dachss  
MainCharacter.PawnPlayer.Flora\_Guillen

To find the right sections of these entries search for “\<object type="object"\>”. You will be taken to a section that look like this:

```xml
<object type="object">
            <value name="hidIndex" type="UInt32">0</value>
            <value hash="BF9B3A5C" type="String">graphics\characters\buddies\nasreen\nasreen.xbg</value>
            <value name="objModel" type="Hash">71BCC1D7</value>
            <value name="hidMeshName" type="String"></value>
            <value hash="E1A0EE56" type="String"></value>
            <value name="hidNodeName" type="Hash">FFFFFFFF</value>
            <value hash="0D9C8B1A" type="String"></value>
            <value name="hidNodeNameLOD0" type="Hash">FFFFFFFF</value>
            <value name="hidDetailObject" type="Bool">True</value>
</object>
<object type="object">
            <value name="hidIndex" type="UInt32">1</value>
            <value hash="BF9B3A5C" type="String">graphics\characters\buddies\warren\warren_avatar.xbg</value>
            <value name="objModel" type="Hash">1519F107</value>
            <value name="hidMeshName" type="String"></value>
            <value hash="E1A0EE56" type="String"></value>
            <value name="hidNodeName" type="Hash">FFFFFFFF</value>
            <value hash="0D9C8B1A" type="String"></value>
            <value name="hidNodeNameLOD0" type="Hash">FFFFFFFF</value>
            <value name="hidDetailObject" type="Bool">True</value>
</object>
```

We are going to edit the second half of this section which by default is directing to the Warren Clyde model. There are two lines we need to change to do this:

1. The "BF9B3A5C" needs to be changed to the file directory of our edited .xbg file.

2. The "objModel" needs to be changed to a CRC32B hash. This can be generated by entering the file directory into a CRC32B hash generator. There are several of these available online and you can google this, I used one available [here](https://md5calc.com/hash/crc32b). To use the one I linked to you paste your file directory (e.g. patch\_unpack\\graphics\\actors\\buddy\_floraguillen\\floraguillen\_avatar.xbg) into the “String to encode” box and then press “Encode”. Your hash is then shown in the “CRC32B encoded string” box.

Below I will give these completed sections that you can paste into your file:

Nasreen

```xml
<object type="object">
            <value name="hidIndex" type="UInt32">1</value>
            <value hash="BF9B3A5C" type="String">graphics\actors\buddy_nasreendavar\nasreendavar_avatar.xbg</value>
            <value name="objModel" type="Hash">9DA20FB2</value>
            <value name="hidMeshName" type="String"></value>
            <value hash="E1A0EE56" type="String"></value>
            <value name="hidNodeName" type="Hash">FFFFFFFF</value>
            <value hash="0D9C8B1A" type="String"></value>
            <value name="hidNodeNameLOD0" type="Hash">FFFFFFFF</value>
            <value name="hidDetailObject" type="Bool">True</value>
</object>
```

Michele

```xml
<object type="object">
            <value name="hidIndex" type="UInt32">1</value>
            <value hash="BF9B3A5C" type="String">graphics\actors\buddy_micheledachss\micheledachss_avatar.xbg</value>
            <value name="objModel" type="Hash">CAE54B41</value>
            <value name="hidMeshName" type="String"></value>
            <value hash="E1A0EE56" type="String"></value>
            <value name="hidNodeName" type="Hash">FFFFFFFF</value>
            <value hash="0D9C8B1A" type="String"></value>
            <value name="hidNodeNameLOD0" type="Hash">FFFFFFFF</value>
            <value name="hidDetailObject" type="Bool">True</value>
</object>
```

Flora

```xml
<object type="object">
            <value name="hidIndex" type="UInt32">1</value>
            <value hash="BF9B3A5C" type="String">graphics\actors\buddy_floraguillen\floraguillen_avatar.xbg</value>
            <value name="objModel" type="Hash">74D38315</value>
            <value name="hidMeshName" type="String"></value>
            <value hash="E1A0EE56" type="String"></value>
            <value name="hidNodeName" type="Hash">FFFFFFFF</value>
            <value hash="0D9C8B1A" type="String"></value>
            <value name="hidNodeNameLOD0" type="Hash">FFFFFFFF</value>
            <value name="hidDetailObject" type="Bool">True</value>
</object>
```

Step 6: Removing player character sounds  
xx\_player.xml \< entitylibrarypatchoverride.fcb (\\patch\_unpack\\generated\\)

The game only has sounds for a male character which is out of place when you’re playing as a woman.

Again, these are the titles for the female characters:

MainCharacter.PawnPlayer.Nasreen\_Davar  
MainCharacter.PawnPlayer.Michele\_Dachss  
MainCharacter.PawnPlayer.Flora\_Guillen

To find the sound section for these entries search for this title: \<object hash="8C369C01" type="PostFXSounds"\>

The section should look like this:

```xml
<object hash="8C369C01" type="PostFXSounds">
       <value name="sndtpPostFXSoundType" hash="FBCA8C9E" type="Int32">12</value> <!-- type="BinHex" value="0C000000" -->
       <value name="sndtpPostFXSoundType3D" hash="CFCF6113" type="Int32">-1</value> <!-- type="BinHex" value="FFFFFFFF" -->
       <value name="sndBlurStimSound" hash="9B855391" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndBurnStimSound" hash="1E9DF019" type="String">0x004BF6C4</value> <!-- type="BinHex" value="3078303034424636433400" -->
       <value name="sndCrushStimSound" hash="E7C54E67" type="String">0x004BF6C4</value> <!-- type="BinHex" value="3078303034424636433400" -->
       <value name="sndPierceHeadStimSound" hash="468F245B" type="String">0x004BF6C3</value> <!-- type="BinHex" value="3078303034424636433300" -->
       <value name="sndPierceFrontStimSound" hash="BA2AE49C" type="String">0x004BF6C3</value> <!-- type="BinHex" value="3078303034424636433300" -->
       <value name="sndPierceLeftStimSound" hash="88A3DAA6" type="String">0x004BF6C3</value> <!-- type="BinHex" value="3078303034424636433300" -->
       <value name="sndPierceRightStimSound" hash="7843F22F" type="String">0x004BF6C3</value> <!-- type="BinHex" value="3078303034424636433300" →
```

We are going to remove the sounds for "sndBurnStimSound", "sndCrushStimSound", "sndPierceHeadStimSound", "sndPierceFrontStimSound", "sndPierceLeftStimSound" and "sndPierceRightStimSound". This involves changing their “String” value to “0xFFFFFFFF”.

Your completed section should look like this:

```xml {5,6,7,8,9,10}
<object hash="8C369C01" type="PostFXSounds">
       <value name="sndtpPostFXSoundType" hash="FBCA8C9E" type="Int32">12</value> <!-- type="BinHex" value="0C000000" -->
       <value name="sndtpPostFXSoundType3D" hash="CFCF6113" type="Int32">-1</value> <!-- type="BinHex" value="FFFFFFFF" -->
       <value name="sndBlurStimSound" hash="9B855391" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndBurnStimSound" hash="1E9DF019" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndCrushStimSound" hash="E7C54E67" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndPierceHeadStimSound" hash="468F245B" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndPierceFrontStimSound" hash="BA2AE49C" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndPierceLeftStimSound" hash="88A3DAA6" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
       <value name="sndPierceRightStimSound" hash="7843F22F" type="String">0xFFFFFFFF</value> <!-- type="BinHex" value="3078464646464646464600" -->
```

