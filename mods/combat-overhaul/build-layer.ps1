# Builds layer\ from the installed game: the merc brain's changes as fragments, which JackAll merges into
# the player's own brain: launchers held for a player in a vehicle, alerts that stand down, squad tactics
# by where the player is, and short exposures from cover. README.md lists each change.
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

$work = Join-Path ([IO.Path]::GetTempPath()) 'combat-overhaul-brain'
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

# Points every connection $outlet of $from makes at $targets' $anchor instead.
function Redirect($from, $outlet, [string[]]$targets, $anchor = 'Start') {
    (Outlet $from $outlet).InnerXml = Connect $targets $anchor
}

# Copies the instance $from as $to into $plan after $after, with each connection to a key of $targets
# made to that key's target instead, or dropped for $null; without $targets, every one is dropped.
function Copy-Instance($from, $to, $plan, $after, $targets = $null) {
    $copy = (Instance $from).CloneNode($true)
    $copy.SetAttribute('Name', $to)
    foreach ($connection in @($copy.SelectNodes('*/Connection'))) {
        $old = $connection.GetAttribute('Target')
        if ($null -ne $targets -and -not $targets.ContainsKey($old)) { continue }
        if ($null -eq $targets -or $null -eq $targets[$old]) { $connection.ParentNode.RemoveChild($connection) | Out-Null }
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
# at him, this many metres across his line to the target where the game has 7, to a side picked at
# random, and goes after a target above him from a firing spot or a way up.
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
# The agent flag the plugin sets while the player is in the open for the soldier; the path too long to
# run with him there; how long the soldier then holds; and how far he looks for and runs to cover with
# him behind cover.
$inTheOpen = 34
$farFromCover = 20
$holdSeconds = 3
$nearCover = 30
# The band whose tasks the new ones are copied from.
$template = "$combat/MediumRangedAttack/AssaultAttack"

# Adds to $plan, after $after, a task that starts $ifSet while the player is in the open for the soldier
# and $ifClear otherwise.
function Add-IfInTheOpen($name, $plan, $after, [string[]]$ifSet, [string[]]$ifClear) {
    Copy-Instance "$template/NoCoverOrNoPathFailure/CheckIsUnderFire" $name $plan $after
    Set-Parameter $name FlagFieldValue $inTheOpen
    Redirect $name IS_TRUE $ifSet
    Redirect $name IS_FALSE $ifClear
}

# Shared by every plan that starts them: the soldier crouches, crouches if the player is in the open,
# fires where he stands, and aims and looks at his target.
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
$ifOpenCrouch = "$combat/IfInTheOpenCrouch"
Add-IfInTheOpen $ifOpenCrouch $combat $crouch $crouch @()
$fireStill = "$combat/FireWhereHeStands"
Copy-Instance "$template/FireNotMoving2" $fireStill $combat $ifOpenCrouch
$aim = "$combat/AimAtTarget"
Copy-Instance "$template/NoCoverOrNoPathFailure/FlankAttack_KR1/AimSmartTarget" $aim $combat $fireStill
$look = "$combat/LookAtTarget"
Copy-Instance "$template/NoCoverOrNoPathFailure/FlankAttack_KR1/LookSmartTarget" $look $combat $aim

# Adds to $plan, after $after, a hold: the soldier stays where he stands, crouched and firing, and starts
# $plan over after $holdSeconds or once the player leaves the open. Returns what starts it.
function Add-Hold($plan, $after) {
    Copy-Instance "$template/WaitCovered" "$plan/HoldInTheOpen" $plan $after
    Set-Parameter "$plan/HoldInTheOpen" timeToWait $holdSeconds
    Redirect "$plan/HoldInTheOpen" Success $plan Restart
    Copy-Instance "$template/NoCoverOrNoPathFailure/ScanReloadIsNeeded1/ScanNeedToRelocate" "$plan/WhileInTheOpen" `
        $plan "$plan/HoldInTheOpen"
    Set-Parameter "$plan/WhileInTheOpen" FlagFieldValue $inTheOpen
    Set-Parameter "$plan/WhileInTheOpen" checkExistance 0
    Redirect "$plan/WhileInTheOpen" Success $plan Restart
    $crouch, $fireStill, $aim, $look, "$plan/HoldInTheOpen", "$plan/WhileInTheOpen"
}

# Adds to $plan, after $after, the suppressor's search for cover, $nearCover wide and at any distance from
# the player, whose find is the fact $fact; it starts $found, or $none when there is none.
function Add-NearCoverSearch($name, $plan, $after, $fact, $found, $none) {
    Copy-Instance "$combat/MediumRangedAttack/SuppressAttack/SuppressFireAttack/FindCoverToMakeSuppressFireLOS" `
        $name $plan $after
    Set-Parameter $name coverFoundFactName $fact
    Set-Parameter $name maxRadius $nearCover
    Set-Parameter $name minRadiusConst 0
    Set-Parameter $name maxRadiusConst 1000
    Redirect $name Success $found
    Redirect $name FindCoverFailed $none
    Redirect $name UnknownError $none
}

# Adds to $plan, after $after, a task that starts $near when the path the soldier found runs at most
# $metres, and $far otherwise.
function Add-PathSplit($name, $plan, $after, $metres, $near, $far) {
    Copy-Instance "$template/AnalyzePath" $name $plan $after
    Set-Parameter $name ShortPathValue $metres
    Set-Parameter $name MediumPathValue $metres
    Redirect $name ShortPath $near
    Redirect $name MediumPath $near
    Redirect $name LongPath $far
}

# Splits $plan's long paths to cover at $farFromCover: a longer one with the player in the open starts
# $hold instead. The game's medium paths end at 15 m for an assaulter and 9 m for a suppressor, inside it.
function Add-FarFromCover($plan, [string[]]$hold) {
    Add-PathSplit "$plan/IfFarFromCover" $plan "$plan/WhileInTheOpen" $farFromCover `
        "$plan/FollowPathLong" "$plan/IfInTheOpenLong"
    Add-IfInTheOpen "$plan/IfInTheOpenLong" $plan "$plan/IfFarFromCover" $hold "$plan/FollowPathLong"
    Redirect "$plan/AnalyzePath" LongPath "$plan/IfFarFromCover"
}

# An assaulter at his cover fires from it as a suppressor does, with the suppressor's pop-up, strafe and
# stand-up plans; a shotgunner still rushes on. With the player in the open, a soldier whose cover is too
# far holds and fires crouched, as does a suppressor with none; with him behind cover, a soldier runs to
# his cover, else to any a short run off, else out to the player's side and in at him from there.
# README.md has the whole of it.
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
        Also-Start @none $ifOpenCrouch
    }
    # He looks for cover as a suppressor does, behind something he can fire from, not a firing spot.
    foreach ($search in 'FindCoverWithLOS', 'FindCoverNoLOS') {
        Set-Parameter "$assault/FindCover/$search" coverHeuristicFilterFactName UnknownFact
        Set-Parameter "$assault/FindCover/$search" coverHeuristicFilter 3
    }
    Redirect "$assault/AmIUsingShotgun" PlayerNotDoingAction "$assault/FindCoverAttackWithLOS"
    Redirect "$assault/CheckForShotgunProgression" IS_FALSE "$assault/FindCoverAttackWithLOS"
    Also-Start "$assault/NoCoverOrNoPathFailure/FlankAttack_KR1/FollowPathToFlankPos" Success $ifOpenCrouch
    Also-Start "$assault/NoCoverOrNoPathFailure/FleeFromLocation_BP1/AfterFleeFireStrategy1" OnStart $ifOpenCrouch

    Add-FarFromCover $assault (Add-Hold $assault "$assault/CTaskChooseCoverAttack1")
    $noCover = "$assault/NoCoverOrNoPathFailure"
    Add-IfInTheOpen "$assault/IfInTheOpenNoCover" $assault "$assault/IfInTheOpenLong" $noCover "$assault/FindCoverNear"
    Redirect "$assault/FindCover" Default "$assault/IfInTheOpenNoCover"
    Add-NearCoverSearch "$assault/FindCoverNear" $assault "$assault/IfInTheOpenNoCover" coverPosition `
        "$assault/FindPathToNearCover" $noCover
    Copy-Instance "$assault/FindPath" "$assault/FindPathToNearCover" $assault "$assault/FindCoverNear" @{
        "$assault/CTaskCheckUnderFire2" = "$assault/IfNearEnough"
    }
    Add-PathSplit "$assault/IfNearEnough" $assault "$assault/FindPathToNearCover" $nearCover `
        "$assault/CTaskCheckUnderFire2" $noCover
    # From the side, a soldier comes in at a player behind cover as the game's rush does.
    if ($flankWidth.ContainsKey($band)) {
        Add-IfInTheOpen "$noCover/IfInTheOpenAfterFlank" $noCover "$noCover/FlankAttack_KR1" "$noCover/WaitCovered1" `
            "$noCover/CTaskOperateOnFlagField1"
        Redirect "$noCover/FlankAttack_KR1" Success "$noCover/IfInTheOpenAfterFlank"
    }

    Set-Parameter "$suppress/FindCoverToMakeSuppressFireLOS" maxRadius $nearCover
    $hold = Add-Hold $suppress "$suppress/AnalyzePath"
    Add-FarFromCover $suppress $hold
    Add-IfInTheOpen "$suppress/IfInTheOpenNoCover" $suppress "$suppress/IfInTheOpenLong" $hold "$suppress/FindCoverNear"
    Redirect "$suppress/SetIsSuppressing" OnStart "$suppress/IfInTheOpenNoCover"
    Add-NearCoverSearch "$suppress/FindCoverNear" $suppress "$suppress/IfInTheOpenNoCover" GoodCoverToMakeSuppressFire `
        "$suppress/FindPathToNearCover" "$suppress/NoCoverMoveAttack"
    Copy-Instance "$suppress/FindPathToCover" "$suppress/FindPathToNearCover" $suppress "$suppress/FindCoverNear" @{
        "$suppress/SetIsSuppressing" = "$suppress/NoCoverMoveAttack"
        "$suppress/LookAtDest1"      = "$suppress/IfNearEnough"
    }
    Add-PathSplit "$suppress/IfNearEnough" $suppress "$suppress/FindPathToNearCover" $nearCover `
        "$suppress/LookAtDest1" "$suppress/NoCoverMoveAttack"
}
$runs = @("$combat/NoCoverRunToThreatPlan/NoCoverRunToThreat_BP1") +
    ($bands | ForEach-Object { "$combat/$_/SuppressAttack/SuppressFireAttack/StrafeAttack/NoCoverRunToThreat_BP1" })
foreach ($run in $runs) {
    foreach ($flank in 'FlankAttack_SR', 'FlankAttack_KR') {
        Also-Start "$run/$flank/FollowPathToFlankPos" Success $ifOpenCrouch
    }
    Start-With "$run/ScanForTarget" $ifOpenCrouch
}

# A cover attack shows the soldier for $exposure seconds at a time, the pop-up twice, down $downSeconds and
# a random $downRandom more between; a round cracking past him at it while he fights the player ducks him
# for $duckSeconds and a random $duckRandom more, then ends it. His next cover search prefers another spot.
$exposure = 1.5
$downSeconds = 2
$downRandom = 1
$popUpSeconds = 2 * ($exposure + $downSeconds + $downRandom)
$duckSeconds = 1.5
$duckRandom = 1
$crack = "$combat/CombatSnipingPlan/ShootPhase/ManageUnderFire/ScanUnderFire"

# Adds the duck to the cover attack $plan, halting its own $timers while he is down.
function Add-Duck($plan, [string[]]$timers) {
    Copy-Instance $crack "$plan/Crack" $plan "$plan/$($timers[0])"
    (Instance "$plan/Crack").SetAttribute('Independent', '1')
    Set-Parameter "$plan/Crack" TimeTolerance 0.25
    Redirect "$plan/Crack" Success "$plan/CrackAtPlayer"
    Add-Instance $plan "$plan/Crack" (IfAtPlayer "$plan/CrackAtPlayer" (
        (Connect $crouch, "$plan/Duck") + (Connect ($timers | ForEach-Object { "$plan/$_" }) 'Stop')) '')
    Copy-Instance "$template/WaitCovered" "$plan/Duck" $plan "$plan/CrackAtPlayer"
    Set-Parameter "$plan/Duck" timeToWait $duckSeconds
    Set-Parameter "$plan/Duck" randomStartDelay $duckRandom
    Redirect "$plan/Duck" Success $plan Stop
    Start-With $plan "$plan/Crack"
}

# Each cover attack: the wait he fires through, and the timers a duck halts.
$coverAttacks = @{
    PopUpAttack   = 'WaitStand', ('WaitCrouch', 'WaitStand')
    StrafeAttack  = 'CTaskWait1', ('CTaskWait1', 'CTaskWait2', 'FollowPath')
    StandUpAttack = 'CTaskWait1', @('CTaskWait1')
}
foreach ($band in $bands) {
    $suppress = "$combat/$band/SuppressAttack/SuppressFireAttack"
    foreach ($attack in $coverAttacks.Keys) {
        Set-Parameter "$suppress/$attack/$($coverAttacks[$attack][0])" timeToWait $exposure
        Add-Duck "$suppress/$attack" $coverAttacks[$attack][1]
    }
    Set-Parameter "$suppress/PopUpAttack/WaitCrouch" timeToWait $downSeconds
    Set-Parameter "$suppress/PopUpAttack/WaitCrouch" randomStartDelay $downRandom
    Set-Parameter "$suppress/PopUpAttack/CTaskWait1" timeToWait $popUpSeconds
    Set-Parameter "$suppress/FindCoverToMakeSuppressFireLOS" canUseSameCover 0
    Set-Parameter "$combat/$band/AssaultAttack/FindCover/FindCoverWithLOS" canUseSameCover 0
}

# Replaces each of the parameters $parameters of the instance $to with $from's.
function Copy-Parameter($from, $to, [string[]]$parameters) {
    foreach ($parameter in $parameters) {
        $old = (Instance $to).SelectSingleNode("Parameter[@Name='$parameter']")
        $new = (Instance $from).SelectSingleNode("Parameter[@Name='$parameter']").CloneNode($true)
        $old.ParentNode.ReplaceChild($new, $old) | Out-Null
    }
}

# Switches on the optional parameter $parameter of the instance $name at $value.
function Set-Switched($name, $parameter, $value) {
    $node = (Instance $name).SelectSingleNode("Parameter[@Name='$parameter']")
    $node.SetAttribute('Value', "<$value, True>")
    $node.SelectSingleNode("Parameter[@Name='Value']").SetAttribute('Value', $value)
    $node.SelectSingleNode("Parameter[@Name='Enabled']").SetAttribute('Value', 1)
}

# On the agent flag the plugin sets, a soldier fighting the player throws his grenade: the flag asks combat
# for its throw, which the game never asks for, wherever the flare's flag is watched. The throw goes at the
# spot he last saw his target, as fast across as the alert brain's, but lobbed to come down at 45 degrees
# over the cover he hides behind, up to 3.5 s in the air; where that arc is blocked, a higher lob at 65,
# then a flatter throw at 30.
$throwFlag = 35
$flare = "$combat/ScanFactShootFlare"
$askThrow = "$combat/ScanThrowRequest"
Copy-Instance $flare $askThrow $combat $flare
Set-Parameter $askThrow FlagFieldValue $throwFlag
Redirect $askThrow Success "$combat/ClearThrowRequest", "$combat/ThrowRequested"
Copy-Instance "$template/NoCoverOrNoPathFailure/CTaskOperateOnFlagField1" "$combat/ClearThrowRequest" $combat $askThrow
Set-Parameter "$combat/ClearThrowRequest" FlagFieldValue $throwFlag
Set-Parameter "$combat/ClearThrowRequest" OperationType 1
Copy-Instance "$shoot/LauncherInHand" "$combat/ThrowRequested" $combat "$combat/ClearThrowRequest"
Set-Parameter "$combat/ThrowRequested" factNameIN ThrowGrenade
Redirect "$combat/ThrowRequested" Success $combat Restart
foreach ($connection in @($doc.SelectNodes("//Connection[@Target='$flare']"))) {
    $copy = $connection.CloneNode($true)
    $copy.SetAttribute('Target', $askThrow)
    $connection.ParentNode.InsertAfter($copy, $connection) | Out-Null
}
$throw = "$combat/CombatThrowProjectile"
$alertThrow = "$alert/GoThrowProjectile"
$compute = "$throw/ComputeProjectileTrajectory"
Copy-Parameter "$alertThrow/PrepareThrow1" $compute throwPosFactName, MaxHorThrowSpeed
Set-Switched $compute ApproachAngle -45
Set-Switched $compute MaxFlyingTime 3.5
$previous = $compute
foreach ($retry in @{ Name = 'ComputeHigherLob'; Angle = -65 }, @{ Name = 'ComputeFlatterThrow'; Angle = -30 }) {
    Copy-Instance $previous "$throw/$($retry.Name)" $throw $previous @{}
    Set-Switched "$throw/$($retry.Name)" ApproachAngle $retry.Angle
    Redirect $previous Failure "$throw/$($retry.Name)"
    $previous = "$throw/$($retry.Name)"
}
Copy-Parameter "$alertThrow/CTaskSetAimStrategy1" "$throw/SetAimAtTarget" aimStrategy, SmartAimPos
Copy-Parameter "$alertThrow/CTaskSetLookStrategy1" "$throw/SetLookAtTarget" lookStrategy, SmartLookPos

$doc.Save($source)
Invoke-Cli ai fragments $source --base $brain --out $fragments
Remove-Item -Recurse -Force $work
