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

$plan = '::MercBrain/MercBrain/MercBrainCombat/CombatSnipingPlan'
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

$doc.Save($source)
$brains = "$layer\mods\scripts\game\newbrains"
New-Item -ItemType Directory -Force $brains | Out-Null
Invoke-Cli ai pack $source -o "$brains\mercbrain.ai.rml"
Remove-Item -Recurse -Force $work
