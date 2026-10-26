# Builds layer\ from the installed game: the merc brain, with soldiers carrying an RPG or a Carl Gustav
# firing it at the player only in a vehicle, and drawing their other gun at the player on foot.
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
$layer = "$PSScriptRoot\layer"
foreach ($dir in $work, $layer) { if (Test-Path $dir) { Remove-Item -Recurse -Force $dir } }

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

# Draws the primary, or the secondary when there is no primary.
function DrawOtherGun($name, $then) {
    @"
<Task Name="$name" Class="CTaskSwitchWeapon" Looping="0" Independent="0">
  <Parameter Name="weaponSelected" Value="primary" />
  <Parameter Name="desiredWeaponClass" Value="" />
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

# At the player, $launcher only while they are in a vehicle, else the other gun and then $next; anyone
# else gets $launcher. With -Watch, the player getting out later stops $launcher too.
function Add-PlayerGate($scope, $after, $launcher, $next, [switch]$Watch) {
    $inVehicle = if ($Watch) { '' } else { Connect "$scope/TargetOnFoot" 'Stop' }
    $onFootStops = @("$scope/TargetInVehicle") + @(if ($Watch) { $launcher })
    Add-Instance $scope $after (IfAtPlayer "$scope/IfAtPlayer" (Connect "$scope/TargetInVehicle", "$scope/TargetOnFoot") (Connect $launcher))
    Add-Instance $scope "$scope/IfAtPlayer" (InVehicle "$scope/TargetInVehicle" 0 ($inVehicle + (Connect $launcher)))
    Add-Instance $scope "$scope/TargetInVehicle" (InVehicle "$scope/TargetOnFoot" 1 ((Connect $onFootStops 'Stop') + (Connect "$scope/DrawOtherGun")))
    Add-Instance $scope "$scope/TargetOnFoot" (DrawOtherGun "$scope/DrawOtherGun" (Connect $next))
}

$plan = '::MercBrain/MercBrain/MercBrainCombat/CombatSnipingPlan'
$shoot = "$plan/ShootPhase"

# In the shoot phase, a launcher in hand is remembered and goes through the gate; the other gun fires
# through the plain sniper loop.
Retarget "$shoot/CheckRocketWeapon" PlayerUsingWeapon "$shoot/RocketShootingLoop" "$shoot/LauncherInHand"
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
Add-PlayerGate $shoot "$shoot/LauncherInHand" "$shoot/RocketShootingLoop" "$shoot/CheckForcedMiss" -Watch

# At the plan's start, a soldier known to carry a launcher goes through the gate before drawing it.
Retarget "$plan/SetFireSpecial" Success "$plan/SwitchToSniperGun" "$plan/IfLauncherKnown"
Add-Instance $plan "$plan/SetFireSpecial" @"
<Task Name="$plan/IfLauncherKnown" Class="CTaskCheckFactExist" Looping="0" Independent="0">
  <Parameter Name="factNameIN" Value="CarriesLauncher" />
  <Parameter Name="useWorldBlackboard" Value="0" />
  <Parameter Name="BlackboardType" Value="1" />
  <Parameter Name="checkType" Value="0" />
  <Parameter Name="factValue" Value="integer">
    <Parameter Name="integer" Value="0" />
  </Parameter>
  <Anchor Name="OnStart" />
  <Event Name="False">$(Connect "$plan/SwitchToSniperGun")</Event>
  <Event Name="True">$(Connect "$plan/IfAtPlayer")</Event>
</Task>
"@
Add-PlayerGate $plan "$plan/IfLauncherKnown" "$plan/SwitchToSniperGun" "$plan/CheckMortarWeapon1"

$doc.Save($source)
$brains = "$layer\mods\scripts\game\newbrains"
New-Item -ItemType Directory -Force $brains | Out-Null
Invoke-Cli ai pack $source -o "$brains\mercbrain.ai.rml"
Remove-Item -Recurse -Force $work
