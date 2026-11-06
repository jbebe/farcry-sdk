# Builds layer\ from the installed game: the merc brain's changes as fragments, which JackAll merges
# into the player's own brain. Soldiers carrying an RPG or a Carl Gustav fire it at the player only in
# a vehicle and draw their other gun at the player on foot, an alerted soldier stands down after
# 90 seconds, the squad tactics the game ships switched off at long and medium range, or when a
# soldier's position is given away, are on, assaulters fire from cover, and with no cover near a
# soldier crouches to fire.
param(
    [string]$Game = 'C:\Games\Far Cry 2',
    [string]$Cli = "$PSScriptRoot\..\..\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
)
$ErrorActionPreference = 'Stop'

# The CLI reports progress on stderr, which Windows PowerShell would otherwise stop the script on.
function Invoke-Cli {
    $ErrorActionPreference = 'Continue'
    & $Cli @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "jackall-cli $($args[0]) $($args[1]) failed" }
}

$work = Join-Path ([IO.Path]::GetTempPath()) 'difficulty-overhaul-brain'
$fragments = "$PSScriptRoot\layer\mods\scripts\game\newbrains\mercbrain.ai.rml"
foreach ($dir in $work, $fragments) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }

Invoke-Cli archive extract "$Game\Data_Win32\worlds\worlds.fat" --names --filter mercbrain.ai.rml -o $work
$brain = (Get-ChildItem -Recurse $work -Filter mercbrain.ai.rml).FullName
Invoke-Cli ai unpack $brain
$source = $brain -replace '\.ai\.rml$', '.source.xml'

$doc = New-Object Xml.XmlDocument
$doc.Load($source)

function Instance($name) {
    $node = $doc.DocumentElement.SelectSingleNode("*[@Name='$name']")
    if ($null -eq $node) { throw "no instance $name" }
    $node
}

# Points the connection that $outlet (an Exit or Event of $from) makes to $old at $new instead.
function Retarget($from, $outlet, $old, $new) {
    $connection = (Instance $from).SelectSingleNode("*[@Name='$outlet']/Connection[@Target='$old']")
    if ($null -eq $connection) { throw "no connection from $from $outlet to $old" }
    $connection.SetAttribute('Target', $new)
}

# Inserts $xml after the instance $after and adds it to $plan.
function Add-Instance($plan, $after, $xml) {
    $fragment = $doc.CreateDocumentFragment()
    $fragment.InnerXml = $xml
    $name = $fragment.FirstChild.GetAttribute('Name')
    $doc.DocumentElement.InsertAfter($fragment, (Instance $after)) | Out-Null
    $add = $doc.CreateElement('Add')
    $add.SetAttribute('Task', $name)
    (Instance $plan).AppendChild($add) | Out-Null
}

function Connect([string[]]$targets, $anchor = 'Start') {
    ($targets | ForEach-Object { "<Connection Target=`"$_`" TargetAnchor=`"$anchor`" />" }) -join ''
}

# The Exit, Event or Anchor $outlet of the instance $from.
function Outlet($from, $outlet) { (Instance $from).SelectSingleNode("*[@Name='$outlet']") }

# Starts $targets too whenever $from's $outlet fires.
function Also-Start($from, $outlet, [string[]]$targets) {
    $fragment = $doc.CreateDocumentFragment()
    $fragment.InnerXml = Connect $targets
    (Outlet $from $outlet).AppendChild($fragment) | Out-Null
}

# Starts $targets whenever the instance $name starts.
function Start-With($name, [string[]]$targets) { Also-Start $name OnStart $targets }

# Points every connection $outlet of $from makes at $targets instead.
function Redirect($from, $outlet, [string[]]$targets) { (Outlet $from $outlet).InnerXml = Connect $targets }

# Copies the instance $from as $to into $plan after $after, with each connection to a key of $targets
# made to that key's target instead, or dropped for $null.
function Copy-Instance($from, $to, $plan, $after, $targets) {
    $copy = (Instance $from).CloneNode($true)
    $copy.SetAttribute('Name', $to)
    foreach ($connection in @($copy.SelectNodes('*/Connection'))) {
        $old = $connection.GetAttribute('Target')
        if (-not $targets.ContainsKey($old)) { continue }
        if ($null -eq $targets[$old]) { $connection.ParentNode.RemoveChild($connection) | Out-Null }
        else { $connection.SetAttribute('Target', $targets[$old]) }
    }
    Add-Instance $plan $after $copy.OuterXml
}

function Set-Parameter($name, $parameter, $value) {
    (Instance $name).SelectSingleNode("Parameter[@Name='$parameter']").SetAttribute('Value', $value)
}

# Sends the CBlueprintDecision $name down its IS_TRUE exit.
function Switch-On($name) { Set-Parameter $name DecisionToUse 1 }

# Whether the target is in a working vehicle, or with $invert 1, not in one.
function InVehicle($name, $invert, $onSuccess) {
    @"
<Scanner Name="$name" Class="CScannerIsInVehicle" Looping="0" Independent="0">
  <Parameter Name="factPawnAgentId" Value="BestTarget" />
  <Parameter Name="functionalVehicleOnly" Value="1" />
  <Parameter Name="movingVehicleOnly" Value="0" />
  <Parameter Name="minMovingSpeed" Value="3" />
  <Parameter Name="invertResult" Value="$invert" />
  <Parameter Name="checkDelay" Value="0.5" />
  <Parameter Name="outFactVehicleAgentId" Value="" />
  <Parameter Name="vehicleType" Value="0" />
  <Anchor Name="OnStart" />
  <Anchor Name="OnStop" />
  <Exit Name="Success">$onSuccess</Exit>
</Scanner>
"@
}

function IfAtPlayer($name, $player, $other) {
    @"
<Task Name="$name" Class="CTaskCheckTargetType" Looping="0" Independent="0">
  <Parameter Name="currentTarget" Value="BestTarget" />
  <Anchor Name="OnStart" />
  <Event Name="IsABuddy">$other</Event>
  <Event Name="IsAPlayer">$player</Event>
  <Event Name="IsASpecialChar">$other</Event>
  <Event Name="IsAMerc">$other</Event>
  <Event Name="IsNotFound">$other</Event>
</Task>
"@
}

function IfCarriesLauncher($name, $no, $yes) {
    @"
<Task Name="$name" Class="CTaskCheckFactExist" Looping="0" Independent="0">
  <Parameter Name="factNameIN" Value="CarriesLauncher" />
  <Parameter Name="useWorldBlackboard" Value="0" />
  <Parameter Name="BlackboardType" Value="1" />
  <Parameter Name="checkType" Value="0" />
  <Parameter Name="factValue" Value="integer">
    <Parameter Name="integer" Value="0" />
  </Parameter>
  <Anchor Name="OnStart" />
  <Event Name="False">$no</Event>
  <Event Name="True">$yes</Event>
</Task>
"@
}

# Draws the $class weapon when he carries one, else $selected; primary falls back to the secondary.
function SwitchWeapon($name, $selected, $class, $then) {
    @"
<Task Name="$name" Class="CTaskSwitchWeapon" Looping="0" Independent="0">
  <Parameter Name="weaponSelected" Value="$selected" />
  <Parameter Name="desiredWeaponClass" Value="$class" />
  <Parameter Name="actionSignal" Value="switch" />
  <Parameter Name="actionSignalGroup" Value="::Pawn Weapons/Weapon Mechanics/States/SwitchingWeapon" />
  <Anchor Name="OnStart" />
  <Anchor Name="OnStop" />
  <Exit Name="Success">$then</Exit>
  <Exit Name="Default" />
  <Exit Name="Always" />
  <Exit Name="WeaponAlreadySelected">$then</Exit>
  <Exit Name="SwitchWeaponFailed" />
</Task>
"@
}

$combat = '::MercBrain/MercBrain/MercBrainCombat'
$plan = "$combat/CombatSnipingPlan"
$shoot = "$plan/ShootPhase"

# In the shoot phase, a soldier with a launcher in hand, or known to carry one, fires it at the player
# only while they are in a vehicle and his other gun otherwise, switching as they get in and out.
Retarget "$shoot/CheckRocketWeapon" PlayerUsingWeapon "$shoot/RocketShootingLoop" "$shoot/LauncherInHand"
Retarget "$shoot/CheckRocketWeapon" PlayerNotDoingAction "$shoot/SniperShootingLoop" "$shoot/IfCarriesLauncher"
Retarget "$shoot/CheckCarlGustavWeapon" PlayerUsingWeapon "$shoot/RocketShootingLoop" "$shoot/LauncherInHand"
Add-Instance $shoot "$shoot/CheckRocketWeapon" @"
<Task Name="$shoot/LauncherInHand" Class="CTaskUpdateBlackboard" Looping="0" Independent="0">
  <Parameter Name="factNameIN" Value="CarriesLauncher" />
  <Parameter Name="newfactNameIN" Value="" />
  <Parameter Name="operationType" Value="1" />
  <Parameter Name="useWorldBlackboardIN" Value="0" />
  <Parameter Name="BlackboardType" Value="1" />
  <Parameter Name="UpdateWho" Value="" />
  <Parameter Name="factTime" Value="-1" />
  <Anchor Name="OnStart" />
  <Anchor Name="OnStop" />
  <Exit Name="Success">$(Connect "$shoot/IfAtPlayer")</Exit>
  <Exit Name="Default" />
  <Exit Name="Always" />
  <Exit Name="Failure" />
</Task>
"@
Add-Instance $shoot "$shoot/LauncherInHand" (IfCarriesLauncher "$shoot/IfCarriesLauncher" (
    Connect "$shoot/SniperShootingLoop") (Connect "$shoot/IfAtPlayer"))
Add-Instance $shoot "$shoot/IfCarriesLauncher" (IfAtPlayer "$shoot/IfAtPlayer" (
    Connect "$shoot/TargetInVehicle", "$shoot/TargetOnFoot") (Connect "$shoot/DrawLauncher"))
Add-Instance $shoot "$shoot/IfAtPlayer" (InVehicle "$shoot/TargetInVehicle" 0 (
    (Connect "$shoot/SniperShootingLoop" 'Stop') + (Connect "$shoot/TargetOnFoot", "$shoot/DrawLauncher")))
Add-Instance $shoot "$shoot/TargetInVehicle" (InVehicle "$shoot/TargetOnFoot" 1 (
    (Connect "$shoot/RocketShootingLoop" 'Stop') + (Connect "$shoot/TargetInVehicle", "$shoot/DrawOtherGun")))
Add-Instance $shoot "$shoot/TargetOnFoot" (SwitchWeapon "$shoot/DrawLauncher" '' special (
    Connect "$shoot/RocketShootingLoop"))
Add-Instance $shoot "$shoot/DrawLauncher" (SwitchWeapon "$shoot/DrawOtherGun" primary '' (
    Connect "$shoot/SniperShootingLoop"))

# At the plan's start, a soldier known to carry a launcher leaves the drawing to the shoot phase.
Retarget "$plan/SetFireSpecial" Success "$plan/SwitchToSniperGun" "$plan/IfCarriesLauncher"
Add-Instance $plan "$plan/SetFireSpecial" (IfCarriesLauncher "$plan/IfCarriesLauncher" (
    Connect "$plan/SwitchToSniperGun") (Connect "$plan/CheckMortarWeapon1"))

$alert = '::MercBrain/MercBrain/MercBrainAlert'

# 90 seconds into an alert that nothing new has restarted, a soldier falls back to where he was
# alerted and out of the alert, as the game has him do only once the player enters a safe zone.
Add-Instance $alert "$alert/CheckFallback1" @"
<Task Name="$alert/StandDownTimer" Class="CTaskWait" Looping="0" Independent="1">
  <Parameter Name="waitTimeFact" Value="" />
  <Parameter Name="timeToWait" Value="90" />
  <Parameter Name="randomStartDelay" Value="0" />
  <Anchor Name="OnStart" />
  <Anchor Name="OnStop" />
  <Exit Name="Success">$(Connect "$alert/StandDown")</Exit>
  <Exit Name="Default" />
  <Exit Name="Always" />
</Task>
"@
Add-Instance $alert "$alert/StandDownTimer" @"
<Task Name="$alert/StandDown" Class="CTaskOperateOnFlagField" Looping="0" Independent="0">
  <Parameter Name="FlagFieldValue" Value="57" />
  <Parameter Name="ArmyFlagFieldValue" Value="0" />
  <Parameter Name="OperationType" Value="0" />
  <Parameter Name="FlagFieldType" Value="0" />
  <Anchor Name="OnStart" />
  <Event Name="IS_TRUE" />
  <Event Name="IS_FALSE" />
  <Event Name="FLAG_SET" />
</Task>
"@
Start-With $alert "$alert/StandDownTimer"

# At long and medium range, a soldier with no cover to reach runs out to the target's side rather than
# at him, this many metres across his line to the target where the game has 7, and goes after a target
# above him from a firing spot or a way up.
$flankWidth = @{ LongRangedAttack = 20; MediumRangedAttack = 15 }
foreach ($band in $flankWidth.Keys) {
    $open = "$combat/$band/AssaultAttack/NoCoverOrNoPathFailure"
    Switch-On "$open/TryRunToPerpendicular"
    Set-Parameter "$open/FlankAttack_KR1/FindPerpendicularPos" PerpendicularLength $flankWidth[$band]
    Switch-On "$combat/$band/CheckForHeightDiff"
}
# Under fire, hurt, or shot at by another target, a soldier moves from a position given away.
foreach ($compromised in 'CombatFireAlert', 'CombatPlayHurt', 'ShootByAnotherTarget') {
    Switch-On "$combat/$compromised/ScanReloadIsNeeded_BP1/ScanRelocate"
    Switch-On "$combat/$compromised/ScanReloadIsNeeded_BP1/ScanRelocate2"
}

$bands = 'KillRangedAttack', 'ShortRangedAttack', 'MediumRangedAttack', 'LongRangedAttack'
# Has the soldier crouch; started beside the task that keeps him where he fires, it runs in that one's plan.
$crouch = "$combat/CrouchInTheOpen"
Add-Instance $combat "$combat/NoCoverRunToThreatPlan" @"
<Task Name="$crouch" Class="CTaskSetPostureIntention" Looping="0" Independent="0">
  <Parameter Name="AIIntention" Value="5" />
  <Anchor Name="OnStart" />
  <Anchor Name="OnStop" />
  <Exit Name="Success" />
  <Exit Name="Default" />
  <Exit Name="Always" />
</Task>
"@
# An assaulter at his cover fires from it as a suppressor does, up over a low wall, out from a corner or
# standing behind a tall one, where he stood beside it; a shotgunner still rushes on. The suppressor's
# pop-up, strafe and stand-up plans run inside his assault; with no cover to fire from, he waits beside it
# as before, crouched. With no cover near, a soldier crouches to fire where he stops: at the end of a run
# to the target's side, after fleeing fire, and where he looks for a target he ran toward.
foreach ($band in $bands) {
    $assault = "$combat/$band/AssaultAttack"
    $suppress = "$combat/$band/SuppressAttack/SuppressFireAttack"
    $after = "$assault/WaitCovered"
    $targets = @{
        "$suppress/FindCoverAttackNOLOS"    = "$assault/FindCoverAttackNOLOS"
        "$suppress/CTaskChooseCoverAttack1" = "$assault/CTaskChooseCoverAttack1"
        "$suppress/NoCoverMoveAttack"       = $null
        "$suppress/TalkAboutSuppressing"    = $null
    }
    foreach ($task in 'FindCoverAttackWithLOS', 'FindCoverAttackNOLOS', 'CTaskChooseCoverAttack1') {
        Copy-Instance "$suppress/$task" "$assault/$task" $assault $after $targets
        $after = "$assault/$task"
    }
    $waiting = (Outlet "$assault/AmIUsingShotgun" PlayerNotDoingAction).InnerXml
    foreach ($none in @("$assault/FindCoverAttackNOLOS", 'NoTarget'), @("$assault/CTaskChooseCoverAttack1", 'NoObstacle')) {
        (Outlet @none).InnerXml = $waiting
        Also-Start @none $crouch
    }
    # He looks for cover as a suppressor does, behind something he can fire from, not a firing spot.
    foreach ($search in 'FindCoverWithLOS', 'FindCoverNoLOS') {
        Set-Parameter "$assault/FindCover/$search" coverHeuristicFilterFactName UnknownFact
        Set-Parameter "$assault/FindCover/$search" coverHeuristicFilter 3
    }
    Redirect "$assault/AmIUsingShotgun" PlayerNotDoingAction "$assault/FindCoverAttackWithLOS"
    Redirect "$assault/CheckForShotgunProgression" IS_FALSE "$assault/FindCoverAttackWithLOS"
    Also-Start "$assault/NoCoverOrNoPathFailure/FlankAttack_KR1/FollowPathToFlankPos" Success $crouch
    Also-Start "$assault/NoCoverOrNoPathFailure/FleeFromLocation_BP1/AfterFleeFireStrategy1" OnStart $crouch
}
$runs = @("$combat/NoCoverRunToThreatPlan/NoCoverRunToThreat_BP1") +
    ($bands | ForEach-Object { "$combat/$_/SuppressAttack/SuppressFireAttack/StrafeAttack/NoCoverRunToThreat_BP1" })
foreach ($run in $runs) {
    foreach ($flank in 'FlankAttack_SR', 'FlankAttack_KR') {
        Also-Start "$run/$flank/FollowPathToFlankPos" Success $crouch
    }
    Start-With "$run/ScanForTarget" $crouch
}
$doc.Save($source)
Invoke-Cli ai fragments $source --base $brain --out $fragments
Remove-Item -Recurse -Force $work
