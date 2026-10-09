# A first look: the question JackAll asks on its first start, and the parts of its window.
$slug = 'tour'

Reset-ShotState
Set-ShotConfig -GamePath ''
Start-ShotApp -NoWait -Width 1440 -Height 900
$question = 'Where is Far Cry 2 installed?'
$dialog = Wait-Ui { Find-ShotWindow $question } -Timeout 60 -What 'the folder question'
Save-Shot $slug '01-first-run' -Window $dialog -Screen
Complete-FileDialog $question (Get-ShotPaths).Game

# The first start checks the archives against a clean Steam 1.03 game, and warns when they differ.
$state = Wait-Ui {
    $box = Find-ShotWindow 'JackAll'
    if ($box) { return $box }
    if ((Get-ShotStatus) -match '^[\d,]+ files across \d+ archives') { return 'loaded' }
} -Timeout 900 -What 'the hash check and the first load'
if ($state -is [System.Windows.Automation.AutomationElement]) {
    $warning = $state
    Write-Host "  warning: $(Get-DialogText $warning)"
    Save-Shot $slug '02-hash-warning' -Window $warning -Screen
    Push-ShotButton $warning 'OK'
}
Wait-ShotReady | Out-Null

$mods = Find-Ui -Scope (Find-Ui -Id MainTabs) -Type TabItem -Name 'Mods' -Children
Save-Shot $slug '03-window' -Callouts $mods, (Get-ShotStatusElement), (Find-Ui -Id ThemePicker)
Stop-ShotApp
