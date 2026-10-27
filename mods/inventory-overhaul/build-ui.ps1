<#
.SYNOPSIS
    Adds the notebook's Inventory page to every retail hud.mgb and writes the changed parts into the
    layer.

.DESCRIPTION
    The page is the HTML mockup's spread (mockup\index.html), authored in the spread's own pixels and
    drawn the way the pause menu's paper pages are: the notebook texture, black Farcry2 ink, common.mgb's
    post-it, tape, red line and brush stroke. Three areas: a_io_notebook places the page on the HUD,
    a_io_page holds it, a_io_postit is the post-it stuck on it. Everything the plugin fills or hides
    is exported as IO_*; a gun's picture is one image per gun and slot, all but one hidden.

    Run from anywhere; reads the retail packages under tmp\, writes layer\mods\ui\localized.
#>
$ErrorActionPreference = "Stop"

$Repo = Resolve-Path "$PSScriptRoot\..\.."
$Cli = "$Repo\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
$Retail = "$Repo\tmp\gamefiles\common\ui\localized"
$Layer = "$PSScriptRoot\layer\mods\ui\localized"
$Work = "$PSScriptRoot\out\ui"

# hud, its main page, and the pause menu's notebook material in common.mgb.
$PackageId = "#A1126B78"
$PageId = "#F2CDBA4C"
$NotebookMaterial = "#F67D9A2B"
$Farcry2 = "#5B34AD62"

# Where the mockup holds the notebook: the spread's centre and width as fractions of the screen.
$CentreX = 0.46
$CentreY = 0.5178
$WidthShare = 0.54375
$Tilt = -1.5

# The spread's place in notebook.png, and the post-it's and an index tab's in postit.png.
$Spread = @{ X = 4; Y = 10; W = 1012; H = 716; Size = 1024 }
$PostItCrop = @{ X = 13; Y = 16; W = 428; H = 311; Size = 512 }
$TabCrop = @{ X = 40; Y = 40; W = 70; H = 180; Size = 512 }

# The mockup's colours.
$Ink = "2A1E14"
$PencilRed = "8F2C1C"
# A gun's condition is a word in its colour, never a number. Each is its own text, shown or hidden,
# since a colour written into a text at runtime does not stay.
$Conditions = [ordered]@{ new = "2F6B2A"; used = "6F6412"; worn = "B0560F"; failing = "A51D14" }

# Every gun picture the game has, by the name gun_icon_<name>.png carries; the plugin asks for
# IO_HAND1_PIC_<name> and so on.
$GunPictures = "6p9 ak47 as50 carlgust deagle dragunov flare fnfal g3ka4 ied ithaca lpo50 m16 m1903 m249 m79 mac10 makarov mgl40 mp5sd p45 rpg7 rpk sniperdart spas12 usas uzi" -split ' '
$Ratings = "Damage", "Range", "Accuracy", "Reliability", "Fire rate"

# A colour as Magma stores it, red in the lowest byte: written AABBGGRR. `rgb` is RRGGBB.
function Colour([string]$rgb, [double]$alpha = 1) {
    "{0:X2}{1}{2}{3}" -f [int][Math]::Round($alpha * 255), $rgb.Substring(4, 2), $rgb.Substring(2, 2), $rgb.Substring(0, 2)
}

# A coordinate as Magma stores it: u16, a negative one wrapped.
function U16([double]$v) { $i = [int][Math]::Round($v); if ($i -lt 0) { 65536 + $i } else { $i } }

function F([double]$v) { $v.ToString("0.######", [Globalization.CultureInfo]::InvariantCulture) }

function Element([string]$slot, [string]$type, [string]$name, [string]$state, [string]$body) {
    @"
<Element slot="$slot" type="$type" HIDDEN="false" ISDUPLICATABLE="true" MASKMODE="NOMASK">
  <USERDATA name="$name"><PROPERTIES /></USERDATA>
  <KEYFRAMES><Keyframe name="kf_$name" IDX="0" INTERPOLATION="None">$state</Keyframe></KEYFRAMES>
$body
</Element>
"@
}

function Placement([double]$x, [double]$y, [double]$w, [double]$h, [double]$rotation) {
    "ROTATION=`"$(F $rotation)`" ORIGIN.x=`"$(U16 ($w / 2))`" ORIGIN.y=`"$(U16 ($h / 2))`" " +
    "LEFT=`"$(U16 $x)`" RIGHT=`"$(U16 ($x + $w))`" TOP=`"$(U16 $y)`" BOTTOM=`"$(U16 ($y + $h))`""
}

# An image, tinted through its corner colours as stock images are. `crop` is the part of the
# material's texture drawn, as fractions: x, y, width, height.
function Image([string]$name, [string]$material, [string]$package, [double]$x, [double]$y, [double]$w,
               [double]$h, [string]$tint = "FFFFFFFF", [double]$rotation = 0, [double[]]$crop = @(0, 0, 1, 1),
               [string]$blending = "Normal") {
    $state = "<ImageState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"FFFFFFFF`" $(Placement $x $y $w $h $rotation) " +
        "SHADOWCOLOR=`"FFFFFFFF`" SHADOWOFFSETX=`"0`" SHADOWOFFSETY=`"0`" TILING.x=`"$(F $crop[2])`" TILING.y=`"$(F $crop[3])`" " +
        "OFFSET.x=`"$(F $crop[0])`" OFFSET.y=`"$(F $crop[1])`" FLIPHORIZONTAL=`"false`" FLIPVERTICAL=`"false`" ACTUALSIZE=`"false`" " +
        "COLOR1=`"$tint`" COLOR2=`"$tint`" COLOR3=`"$tint`" COLOR4=`"$tint`" />"
    $body = "<Image BLENDINGMODE=`"$blending`" ALPHABLENDFIRST=`"false`" ADDRESSINGMODEU=`"Clamp`" ADDRESSINGMODEV=`"Clamp`">" +
        "<MATERIALLINK present=`"true`" id=`"$material`" PACKAGE=`"$package`" /></Image>"
    Element 74 Image $name $state $body
}

# A line of Farcry2 ink, as the pause menu's pages set theirs: no shadow, one line, cut with an
# ellipsis.
function Text([string]$name, [string]$string, [double]$x, [double]$y, [double]$w, [double]$h, [int]$height,
              [string]$colour, [string]$align = "LEFT", [double]$rotation = 0, [string]$alignY = "CENTER") {
    $tracking = if ($height -ge 22) { 0 } elseif ($height -ge 19) { 65516 } else { 65521 }
    $state = "<TextState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"$colour`" $(Placement $x $y $w $h $rotation) " +
        "OFFSETY=`"0`" ABSOFFSETY=`"0`" SHADOWCOLOR=`"FF000000`" HEIGHT=`"$height`" SHADOWOFFSETX=`"0`" SHADOWOFFSETY=`"0`" " +
        "LEADING=`"$height`" TRACKING=`"$tracking`" />"
    $body = "<Text useStringTable=`"false`" STRING=`"$string`" ALIGNMENTX=`"$align`" ALIGNMENTY=`"$alignY`" WRAPPING=`"false`" " +
        "CLIPPING=`"false`" ELLIPSIS=`"true`" AUTOSIZED=`"false`" BOLD=`"false`" ITALICS=`"false`" UNDERLINED=`"false`" " +
        "BLENDINGMODE=`"Normal`" ALPHABLENDFIRST=`"false`"><FONTFAMILY present=`"true`" id=`"$Farcry2`" PACKAGE=`"\fonts.mgb`" /></Text>"
    Element 50 Text $name $state $body
}

function Instance([string]$area, [double]$x, [double]$y, [double]$scaleX, [double]$scaleY, [double]$rotation) {
    $state = "<ScaleState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"FFFFFFFF`" ROTATION=`"$(F $rotation)`" ORIGIN.x=`"0`" ORIGIN.y=`"0`" " +
        "POSITION.x=`"$(U16 $x)`" POSITION.y=`"$(U16 $y)`" SCALEX=`"$(F $scaleX)`" SCALEY=`"$(F $scaleY)`" />"
    $body = "<AreaInstance LABEL=`"`" INDEXOFFSET=`"0`"><MATERIALLINK present=`"false`" />" +
        "<LINK slot=`"150`" TIMING=`"NoTimingStrategy`" PACKAGE=`"$PackageId`" AREA=`"$area`" ISUSINGDUPLICATEDAREA=`"false`" /></AreaInstance>"
    Element 33 AreaInstance $area $state $body
}

# Every area starts with a placeholder named action, as stock areas do.
function Area([string]$name, [double]$w, [double]$h, [string[]]$children) {
    $action = Element 77 Placeholder action `
        '<RectState INTERPOLATIONFLAGS="0" STATECOLOR="FFFFFFFF" ROTATION="0" ORIGIN.x="0" ORIGIN.y="0" LEFT="0" RIGHT="0" TOP="0" BOTTOM="0" />' `
        '<Placeholder />'
    @"
<Area slot="44" type="Area" FRAMERATE="30" CURRENTFRAME="0" STATICBOX="0 $(U16 $w) 0 $(U16 $h)">
  <USERDATA name="$name"><PROPERTIES /></USERDATA>
  <CHILDREN>
$action
$($children -join "`n")
  </CHILDREN>
</Area>
"@
}

function Crop($c) { , @(($c.X / $c.Size), ($c.Y / $c.Size), ($c.W / $c.Size), ($c.H / $c.Size)) }

# A point turned by `degrees` about the origin, as Magma turns an instance about its position.
function Turn([double]$x, [double]$y, [double]$degrees) {
    $r = $degrees * [Math]::PI / 180
    , @(($x * [Math]::Cos($r) - $y * [Math]::Sin($r)), ($x * [Math]::Sin($r) + $y * [Math]::Cos($r)))
}

$Exports = [System.Collections.Generic.List[string]]::new()
$NotebookPath = "$PackageId $PageId a_io_notebook a_io_notebook"
$PagePath = "$NotebookPath a_io_page a_io_page"
$PostItPath = "$PagePath a_io_postit a_io_postit"
function Export([string]$path, [string]$element) {
    $Exports.Add("<GENERICOBJECT name=`"IO_$($element.Substring(5).ToUpperInvariant())`"><LINK slot=`"69`" LASTOBJECTTYPE=`"Element`" IDS=`"$path $element`" /></GENERICOBJECT>")
}

$inkFull = Colour $Ink
$inkSoft = Colour $Ink 0.6
$page = [System.Collections.Generic.List[string]]::new()
$postIt = [System.Collections.Generic.List[string]]::new()

# Index tabs on the right edge, the open one pulled out over the pages. The others go in first so
# the spread covers them.
$tabs = "Inventory", "Papers", "Journal", "Contacts"
$openTab = $null
for ($i = 0; $i -lt $tabs.Count; $i++) {
    $open = $i -eq 0
    $x = if ($open) { 936 } else { 962 }
    $y = 70 + $i * 124
    $tint = if ($open) { "FFFFFFFF" } else { "FFCCCCCC" }
    $name = $tabs[$i].ToLowerInvariant()
    $parts = @(
        (Image "i_io_tab_$name" postit "\common.mgb" $x $y 96 116 $tint 0 (Crop $TabCrop)),
        (Text "t_io_tab_$name" $tabs[$i] ($x + 70 - 58) ($y + 58 - 26) 116 52 22 $inkFull CENTER 90))
    if ($open) { $openTab = $parts } else { $page.AddRange([string[]]$parts) }
}
$page.Add((Image i_io_spread $NotebookMaterial "\common.mgb" 0 0 $Spread.W $Spread.H "FFFFFFFF" 0 (Crop $Spread)))
$page.AddRange([string[]]$openTab)

# The left page: Hands, then Items.
$left = 96
$width = 368
$page.Add((Text t_io_hands "HANDS" $left 52 300 18 18 $inkSoft LEFT 0 TOP))
$redLine = Colour $PencilRed 0.55
$pencil = Colour $PencilRed
# The selected row's mark; the font has no arrow.
$Arrow = [string][char]0xBB
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    $page.Add((Image "i_io_hand${k}_sel" red_line "\common.mgb" ($left + 24) ($top + 22) ($width - 16) 22 $redLine -0.6))
    Export $PagePath "i_io_hand${k}_sel"
}
$page.Add((Image i_io_machete_sel red_line "\common.mgb" ($left + 24) (168 + 22) ($width - 16) 22 $redLine -0.6))
Export $PagePath i_io_machete_sel
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    $page.Add((Image "i_io_hand${k}_ring" crosshair_circle_s "" ($left + 4) ($top + 12) 22 22 $inkFull))
    $page.Add((Text "t_io_hand${k}_num" "$k" ($left + 4) ($top + 12) 22 22 18 $inkFull CENTER))
    foreach ($pic in $GunPictures) {
        $page.Add((Image "i_io_hand${k}_pic_$pic" "io_gun_$pic" "" ($left + 38) ($top + 11) 96 24 "FFFFFFFF" 0 @(0, 0, 1, 1) Multiply))
        Export $PagePath "i_io_hand${k}_pic_$pic"
    }
    $page.Add((Image "i_io_hand${k}_pic_mortar" hud_icon_mortar_shop "" ($left + 38) ($top + 11) 96 24 (Colour "000000" 0.82)))
    Export $PagePath "i_io_hand${k}_pic_mortar"
    $page.Add((Text "t_io_hand${k}_name" "" ($left + 142) ($top + 10) 128 26 22 $inkFull))
    $page.Add((Text "t_io_hand${k}_empty" "empty" ($left + 142) ($top + 10) 128 26 22 $inkSoft))
    Export $PagePath "t_io_hand${k}_name"
    Export $PagePath "t_io_hand${k}_empty"
    foreach ($word in $Conditions.Keys) {
        $page.Add((Text "t_io_hand${k}_$word" $word ($left + 270) ($top + 10) 90 26 20 (Colour $Conditions[$word]) RIGHT))
        Export $PagePath "t_io_hand${k}_$word"
    }
}
$page.Add((Text t_io_machete_mark $Arrow $left 178 30 26 22 $pencil CENTER))
Export $PagePath t_io_machete_mark
$page.Add((Image i_io_machete hud_icon_machete "" ($left + 38) (168 + 11) 96 24 (Colour "000000" 0.82)))
$page.Add((Text t_io_machete "Machete" ($left + 142) 178 218 26 22 $inkFull))

# Items shows a window of eight rows that follows the selection, with how many more lie above it
# beside the heading and below it under the last row.
$page.Add((Text t_io_items "ITEMS" $left 230 300 18 18 $inkSoft LEFT 0 TOP))
$page.Add((Text t_io_items_above "" ($left + 160) 228 ($width - 160) 22 18 (Colour $Ink 0.65) RIGHT))
Export $PagePath t_io_items_above
for ($j = 1; $j -le 8; $j++) {
    $top = 254 + ($j - 1) * 40
    $page.Add((Image "i_io_item${j}_sel" red_line "\common.mgb" ($left + 24) ($top + 18) ($width - 16) 22 $redLine -0.6))
    $page.Add((Text "t_io_item${j}_mark" $Arrow $left ($top + 7) 30 26 22 $pencil CENTER))
    $page.Add((Text "t_io_item$j" "" ($left + 38) ($top + 7) ($width - 46) 26 22 $inkFull))
    foreach ($name in "i_io_item${j}_sel", "t_io_item${j}_mark", "t_io_item$j") { Export $PagePath $name }
}
$page.Add((Text t_io_items_more "" ($left + 38) 576 ($width - 46) 22 18 (Colour $Ink 0.65)))
Export $PagePath t_io_items_more

# The right page's top half: the belt.
$right = 532
$page.Add((Text t_io_belt "BELT" $right 52 300 18 18 $inkSoft LEFT 0 TOP))
$belt = @(
    @{ Name = "Syrettes"; Material = "hud_icon_seringe"; Blending = "Normal" },
    @{ Name = "Malaria pills"; Material = "hud_icon_pills"; Blending = "Normal" },
    @{ Name = "M67 grenades"; Material = "io_gun_m67"; Blending = "Multiply" },
    @{ Name = "Molotovs"; Material = "io_gun_molotov"; Blending = "Multiply" })
for ($j = 1; $j -le $belt.Count; $j++) {
    $top = 76 + ($j - 1) * 40
    $page.Add((Image "i_io_belt${j}_sel" red_line "\common.mgb" ($right + 24) ($top + 18) ($width - 8) 22 $redLine -0.6))
    $page.Add((Text "t_io_belt${j}_mark" $Arrow $right ($top + 7) 30 26 22 $pencil CENTER))
    Export $PagePath "i_io_belt${j}_sel"
    Export $PagePath "t_io_belt${j}_mark"
    $page.Add((Image "i_io_belt$j" $belt[$j - 1].Material "" ($right + 55) ($top + 5) 30 30 "FFFFFFFF" 0 @(0, 0, 1, 1) $belt[$j - 1].Blending))
    $page.Add((Text "t_io_belt$j" $belt[$j - 1].Name ($right + 110) ($top + 7) 190 26 22 $inkFull))
    $page.Add((Text "t_io_belt${j}_count" "" ($right + 300) ($top + 7) 76 26 20 $inkFull RIGHT))
    Export $PagePath "t_io_belt${j}_count"
}

# The post-it, in its own pixels; its instance tilts it about its centre.
$postIt.Add((Image i_io_postit_paper postit "\common.mgb" 0 0 416 302 "FFFFFFFF" 0 (Crop $PostItCrop)))
# scotchtape.png stands upright; it is turned a quarter to lie across the post-it's top.
$postIt.Add((Image i_io_postit_tape scotchtape "\common.mgb" (208 - 17) (-3 - 58) 34 116 (Colour "FFFFFF" 0.85) 92))
foreach ($pic in $GunPictures) {
    $postIt.Add((Image "i_io_postit_pic_$pic" "io_gun_$pic" "" 65 22 288 72 "FFFFFFFF" 0 @(0, 0, 1, 1) Multiply))
    Export $PostItPath "i_io_postit_pic_$pic"
}
foreach ($silhouette in @(@{ Pic = "machete"; Material = "hud_icon_machete" }, @{ Pic = "mortar"; Material = "hud_icon_mortar_shop" })) {
    $postIt.Add((Image "i_io_postit_pic_$($silhouette.Pic)" $silhouette.Material "" 65 22 288 72 (Colour "000000" 0.8)))
    Export $PostItPath "i_io_postit_pic_$($silhouette.Pic)"
}
# The belt's and the wallet's pictures are square.
foreach ($square in @(
        @{ Pic = "diamond"; Material = "ui_hud_icon_diamond"; Tint = (Colour "000000" 0.8); Blending = "Normal" },
        @{ Pic = "seringe"; Material = "hud_icon_seringe"; Tint = "FFFFFFFF"; Blending = "Normal" },
        @{ Pic = "pills"; Material = "hud_icon_pills"; Tint = "FFFFFFFF"; Blending = "Normal" },
        @{ Pic = "m67"; Material = "io_gun_m67"; Tint = "FFFFFFFF"; Blending = "Multiply" },
        @{ Pic = "molotov"; Material = "io_gun_molotov"; Tint = "FFFFFFFF"; Blending = "Multiply" })) {
    $postIt.Add((Image "i_io_postit_pic_$($square.Pic)" $square.Material "" 173 22 72 72 $square.Tint 0 @(0, 0, 1, 1) $square.Blending))
    Export $PostItPath "i_io_postit_pic_$($square.Pic)"
}
$postIt.Add((Text t_io_postit_name "" 26 96 366 26 24 $inkFull))
$postIt.Add((Text t_io_postit_sub "" 26 124 366 18 15 $inkSoft))
# Three lines of a label and a value: a gun's condition and ammo, a belt item's count, then the
# upgrades bought for it.
for ($l = 1; $l -le 3; $l++) {
    $postIt.Add((Text "t_io_postit_line$l" "" 26 (148 + ($l - 1) * 26) 120 24 20 $inkFull))
    Export $PostItPath "t_io_postit_line$l"
}
$postIt.Add((Text t_io_postit_value1 "" 101 148 100 24 20 $inkFull RIGHT))
$postIt.Add((Text t_io_postit_value2 "" 81 174 120 24 20 $inkFull RIGHT))
foreach ($word in $Conditions.Keys) {
    $postIt.Add((Text "t_io_postit_$word" $word 101 148 100 24 20 (Colour $Conditions[$word]) RIGHT))
    Export $PostItPath "t_io_postit_$word"
}
# The game's upgrade icons, faint until bought: a gun's operation and repair manuals and its
# bandolier, or the syrettes' two first aid manuals.
$upgrades = "manual", "repair", "ammo", "ammo"
for ($u = 0; $u -lt $upgrades.Count; $u++) {
    $material = "io_upgrade_$($upgrades[$u])"
    $postIt.Add((Image "i_io_postit_up${u}_faint" $material "" (125 + $u * 27) 201 22 22 (Colour $Ink 0.35)))
    $postIt.Add((Image "i_io_postit_up${u}_bought" $material "" (125 + $u * 27) 201 22 22 (Colour $Ink 0.85)))
    Export $PostItPath "i_io_postit_up${u}_faint"
    Export $PostItPath "i_io_postit_up${u}_bought"
}
$postIt.Add((Text t_io_postit_note "" 26 236 366 22 18 $inkFull))
$postIt.Add((Text t_io_postit_warn "" 26 258 366 22 18 $pencil))
foreach ($name in "t_io_postit_name", "t_io_postit_sub", "t_io_postit_value1", "t_io_postit_value2", "t_io_postit_note", "t_io_postit_warn") {
    Export $PostItPath $name
}
# Each rating is a brush stroke as long as the rating, the way the loadout draws them, over a faint
# full-length one: one stroke per half point, all but one hidden.
for ($r = 1; $r -le $Ratings.Count; $r++) {
    $top = 148 + ($r - 1) * 25
    $postIt.Add((Text "t_io_postit_rating$r" $Ratings[$r - 1] 217 $top 110 22 14 $inkFull))
    $postIt.Add((Image "i_io_postit_rating${r}_track" brush_stroke "\common.mgb" 330 ($top + 4) 58 14 (Colour $Ink 0.18)))
    Export $PostItPath "t_io_postit_rating$r"
    Export $PostItPath "i_io_postit_rating${r}_track"
    for ($half = 1; $half -le 10; $half++) {
        $postIt.Add((Image "i_io_postit_rating${r}_$half" brush_stroke "\common.mgb" 330 ($top + 4) (5.8 * $half) 14 (Colour $Ink 0.85) 0 @(0, 0, ($half / 10), 1)))
        Export $PostItPath "i_io_postit_rating${r}_$half"
    }
}
$page.Add((Instance a_io_postit 0 0 1 1 0))
$postItIndex = $page.Count - 1

$materials = ((($GunPictures + "m67", "molotov") | ForEach-Object {
    "<Material name=`"io_gun_$_`" texture=`"\textures\guns\gun_icon_$_.png`" REGION=`"0 0 1 1`" />"
}) + (("manual", "repair", "ammo") | ForEach-Object {
    "<Material name=`"io_upgrade_$_`" texture=`"\textures\ui_icon_$($_)upgrade.png`" REGION=`"0 0 1 1`" />"
})) -join "`n"

function IndexOf([System.Collections.Generic.List[string]]$lines, [string]$text, [int]$from = 0) {
    for ($i = $from; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Contains($text)) { return $i }
    }
    throw "'$text' not found after line $from"
}

function Attribute([string]$line, [string]$name) {
    if ($line -notmatch "$([regex]::Escape($name))=`"(\d+)`"") { throw "no $name in $line" }
    $v = [int]$Matches[1]
    if ($v -gt 32767) { $v - 65536 } else { $v }
}

Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Work | Out-Null

foreach ($source in Get-ChildItem -Recurse -Path $Retail -Filter hud.mgb) {
    $variant = $source.Directory.Parent.FullName.Substring($Retail.Length + 1)
    $dir = Join-Path $Work $variant
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $source.FullName "$dir\hud.mgb"
    & $Cli mgb decode "$dir\hud.mgb" -o "$dir\hud.xml" | Out-Null
    $pause = "$($source.Directory.FullName)\sp_pause_menus.mgb"
    Copy-Item $pause "$dir\sp_pause_menus.mgb"
    & $Cli mgb decode "$dir\sp_pause_menus.mgb" -o "$dir\pause.xml" | Out-Null
    $lines = [System.Collections.Generic.List[string]]::new([IO.File]::ReadAllLines("$dir\hud.xml"))

    # The screen in canvas units, and the pause menu's notebook on it: its texture stretched over a
    # box whose sides give the canvas units per texture pixel.
    $header = $lines[0]
    $screenX = -(Attribute $header 'DISPLAYOFFSET.x')
    $screenY = -(Attribute $header 'DISPLAYOFFSET.y')
    $screenW = Attribute $header 'PAGESIZE.w'
    $screenH = Attribute $header 'PAGESIZE.h'
    $pauseLines = [IO.File]::ReadAllLines("$dir\pause.xml")
    $notebook = 0
    while ($pauseLines[$notebook] -notmatch "id=`"$NotebookMaterial`"") { $notebook++ }
    $box = $notebook
    while ($pauseLines[$box] -notmatch 'LEFT=') { $box-- }
    $unitX = ((Attribute $pauseLines[$box] RIGHT) - (Attribute $pauseLines[$box] LEFT)) / $Spread.Size
    $unitY = ((Attribute $pauseLines[$box] BOTTOM) - (Attribute $pauseLines[$box] TOP)) / $Spread.Size

    # The mockup's size and place, keeping the pause menu's proportions.
    $shrink = $WidthShare * $screenW / ($Spread.W * $unitX)
    $scaleX = $unitX * $shrink
    $scaleY = $unitY * $shrink
    $turned = Turn ($scaleX * $Spread.W / 2) ($scaleY * $Spread.H / 2) $Tilt
    $x = $screenX + $CentreX * $screenW - $turned[0]
    $y = $screenY + $CentreY * $screenH - $turned[1]

    # The post-it's place on the page, tilted about its centre, in the page's own pixels.
    $postItTurned = Turn 208 151 -1.4
    $children = [System.Collections.Generic.List[string]]::new($page)
    $children[$postItIndex] = Instance a_io_postit (512 + 208 - $postItTurned[0]) (344 + 151 - $postItTurned[1]) 1 1 -1.4

    $areas = @(
        (Area a_io_notebook ($Spread.W + 46) $Spread.H @(Instance a_io_page 0 0 1 1 0)),
        (Area a_io_page ($Spread.W + 46) $Spread.H $children),
        (Area a_io_postit 416 302 $postIt)) -join "`n"
    $objects = @("<GENERICOBJECT name=`"IO_NOTEBOOK`"><LINK slot=`"69`" LASTOBJECTTYPE=`"Element`" IDS=`"$NotebookPath a_io_page`" /></GENERICOBJECT>") + $Exports

    # Generic objects, the areas, the page, then materials: back to front, so each insertion leaves
    # the earlier indices valid.
    $at = IndexOf $lines '<GENERICOBJECTS>'
    $lines.Insert($at + 1, ($objects -join "`n"))

    $strings = IndexOf $lines '<STRINGTABLE'
    $lines.Insert($strings - 1, $areas)

    # Last on the HUD page, so it draws over the rest of the HUD.
    $pageEnd = IndexOf $lines '</CHILDREN>' (IndexOf $lines "<USERDATA name=`"$PageId`">")
    $lines.Insert($pageEnd, (Instance a_io_notebook $x $y $scaleX $scaleY $Tilt))

    $materialsAt = IndexOf $lines '<MATERIALS materialExtra='
    $lines.Insert($materialsAt + 1, $materials)

    [IO.File]::WriteAllLines("$dir\hud.xml", $lines)
    & $Cli mgb verify "$dir\hud.xml"
    if ($LASTEXITCODE -ne 0) { throw "verify failed for $variant" }

    $target = Join-Path $Layer "$variant\ui"
    Remove-Item -Recurse -Force "$target\hud.mgb" -ErrorAction SilentlyContinue
    & $Cli mgb fragments "$dir\hud.xml" --base $source.FullName --out "$target\hud.mgb" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "fragments failed for $variant" }
    Write-Host ("{0,-20} notebook at {1:0},{2:0} scaled {3:0.000} x {4:0.000}" -f $variant, $x, $y, $scaleX, $scaleY)
}
