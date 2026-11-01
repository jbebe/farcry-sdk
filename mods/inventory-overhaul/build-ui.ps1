<#
.SYNOPSIS
    Adds the notebook's pages to every retail hud.mgb and writes the changed parts into the layer.

.DESCRIPTION
    The pages are the HTML mockup's spread (mockup\index.html), authored in the spread's own pixels and
    drawn the way the pause menu's paper pages are: the notebook texture, black Farcry2 ink, common.mgb's
    post-it, tape, red line and brush stroke. The plugin draws them onto the notebook's pages rather than
    the screen: a_io_notebook lays the spread over the screen as the sheet the pages show, and a_io_page
    holds it between two marker images the plugin knows it by, with its index tabs along the top, one
    area per page (a_io_inventory, a_io_papers, a_io_journal, a_io_contacts) shown at a time,
    a_io_portraits the buddies' portraits a row takes its picture from, a_io_paper_pictures the papers'
    pictures, and a_io_postit the post-it stuck on it. Everything the plugin fills or hides is exported as IO_*; a gun's picture is one image
    per gun and slot, all but one hidden.

    Run from anywhere; reads the retail files under tmp\, and the install's for the fonts, writes
    layer\mods\ui.
#>
param(
    [string]$Game = 'C:\Games\Far Cry 2'
)
$ErrorActionPreference = "Stop"

. "$PSScriptRoot\build-common.ps1"
$Retail = "$Repo\tmp\gamefiles\common\ui\localized"
$StockTexture = "$Repo\tmp\gamefiles\common\ui\textures\ui_tape_icon.xbt"
$Texconv = "$Repo\tools\third-party\FCBConverter\texconv.exe"
$Layer = "$PSScriptRoot\layer\mods\ui\localized"
$Textures = "$PSScriptRoot\layer\mods\ui\textures\io"
$Work = "$PSScriptRoot\out\ui"

# hud, its main page, and the pause menu's notebook material in common.mgb, whose id is $CommonId.
$PackageId = "#A1126B78"
$CommonId = "#E5EC7051"
$PageId = "#F2CDBA4C"
$NotebookMaterial = "#F67D9A2B"
$Farcry2 = "#5B34AD62"
# The handwriting: Caveat in its bold, made a font package as the game's are, and drawn smaller than it
# is shown so it is no finer than they are. Leaving Cyrillic out halves its page; no paper uses it.
$CaveatSize = 20
$CaveatWeight = 700
$Caveat = "Caveat_$CaveatSize"

# The spread's place in notebook.png. The sheet the notebook's pages show is the spread from $SheetTop
# above it, room for the tabs; it covers the HUD's whole canvas page, which the plugin copies whole.
$Spread = @{ X = 4; Y = 10; W = 1012; H = 716; Size = 1024 }
$SheetTop = -40

# The post-it's and an index tab's place in postit.png.
$PostItCrop = @{ X = 13; Y = 16; W = 428; H = 311; Size = 512 }
$TabCrop = @{ X = 40; Y = 40; W = 180; H = 70; Size = 512 }

# The mockup's ink, and the red of the pencil line a selected row is underlined with.
$Ink = "2A1E14"
$PencilRed = "8F2C1C"

# Every gun picture the game has, by the name gun_icon_<name>.png carries; the plugin asks for
# IO_POSTIT_PIC_<name>.
$GunPictures = "6p9 ak47 as50 carlgust deagle dragunov flare fnfal g3ka4 ied ithaca lpo50 m16 m1903 m249 m79 mac10 makarov mgl40 mp5sd p45 rpg7 rpk sniperdart spas12 usas uzi" -split ' '
$Ratings = "Damage", "Range", "Accuracy", "Reliability", "Fire rate"
# The buddies' portraits, textures\hud\icons_buddies\buddies_<name>.png.
$Portraits = "andre flora frank hakim josip marty michele nasreen paul singh warren bai" -split ' '

# A colour as Magma stores it, red in the lowest byte: written AABBGGRR. `rgb` is RRGGBB.
function Colour([string]$rgb, [double]$alpha = 1) {
    "{0:X2}{1}{2}{3}" -f [int][Math]::Round($alpha * 255), $rgb.Substring(4, 2), $rgb.Substring(2, 2), $rgb.Substring(0, 2)
}

# A coordinate as Magma stores it: u16, a negative one wrapped.
function U16([double]$v) { $i = [int][Math]::Round($v); if ($i -lt 0) { 65536 + $i } else { $i } }

function F([double]$v) { $v.ToString("0.######", [Globalization.CultureInfo]::InvariantCulture) }

function Element([string]$slot, [string]$type, [string]$name, [string]$state, [string]$body, [bool]$hidden = $false) {
    @"
<Element slot="$slot" type="$type" HIDDEN="$($hidden.ToString().ToLowerInvariant())" ISDUPLICATABLE="true" MASKMODE="NOMASK">
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
               [string]$blending = "Normal", [string]$addressing = "Clamp") {
    $state = "<ImageState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"FFFFFFFF`" $(Placement $x $y $w $h $rotation) " +
        "SHADOWCOLOR=`"FFFFFFFF`" SHADOWOFFSETX=`"0`" SHADOWOFFSETY=`"0`" TILING.x=`"$(F $crop[2])`" TILING.y=`"$(F $crop[3])`" " +
        "OFFSET.x=`"$(F $crop[0])`" OFFSET.y=`"$(F $crop[1])`" FLIPHORIZONTAL=`"false`" FLIPVERTICAL=`"false`" ACTUALSIZE=`"false`" " +
        "COLOR1=`"$tint`" COLOR2=`"$tint`" COLOR3=`"$tint`" COLOR4=`"$tint`" />"
    $body = "<Image BLENDINGMODE=`"$blending`" ALPHABLENDFIRST=`"false`" ADDRESSINGMODEU=`"$addressing`" ADDRESSINGMODEV=`"$addressing`">" +
        "<MATERIALLINK present=`"true`" id=`"$material`" PACKAGE=`"$package`" /></Image>"
    Element 74 Image $name $state $body
}

# A line of Farcry2 ink, as the pause menu's pages set theirs: no shadow, one line, cut with an
# ellipsis; or, with `wrap`, prose wrapped from the top of its box. `hand` writes it in Caveat, the
# notebook's handwriting.
function Text([string]$name, [string]$string, [double]$x, [double]$y, [double]$w, [double]$h, [int]$height,
              [string]$colour, [string]$align = "LEFT", [double]$rotation = 0, [string]$alignY = "CENTER",
              [bool]$wrap = $false, [bool]$italic = $false, [bool]$hand = $false) {
    $family = if ($hand) { $Caveat } else { $Farcry2 }
    $tracking = if ($hand -or $height -ge 22) { 0 } elseif ($height -ge 19) { 65516 } else { 65521 }
    $wrapping = $wrap.ToString().ToLowerInvariant()
    $ellipsis = (-not $wrap).ToString().ToLowerInvariant()
    $italics = $italic.ToString().ToLowerInvariant()
    $state = "<TextState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"$colour`" $(Placement $x $y $w $h $rotation) " +
        "OFFSETY=`"0`" ABSOFFSETY=`"0`" SHADOWCOLOR=`"FF000000`" HEIGHT=`"$height`" SHADOWOFFSETX=`"0`" SHADOWOFFSETY=`"0`" " +
        "LEADING=`"$height`" TRACKING=`"$tracking`" />"
    $body = "<Text useStringTable=`"false`" STRING=`"$string`" ALIGNMENTX=`"$align`" ALIGNMENTY=`"$alignY`" WRAPPING=`"$wrapping`" " +
        "CLIPPING=`"false`" ELLIPSIS=`"$ellipsis`" AUTOSIZED=`"false`" BOLD=`"false`" ITALICS=`"$italics`" UNDERLINED=`"false`" " +
        "BLENDINGMODE=`"Normal`" ALPHABLENDFIRST=`"false`"><FONTFAMILY present=`"true`" id=`"$family`" PACKAGE=`"\fonts.mgb`" /></Text>"
    Element 50 Text $name $state $body
}

# An instance of `area`, named after it unless given a `name`; by default one of hud's own areas.
function Instance([string]$area, [double]$x, [double]$y, [double]$scaleX, [double]$scaleY, [double]$rotation,
                  [string]$name = $area, [string]$package = $PackageId, [string]$timing = "NoTimingStrategy",
                  [bool]$hidden = $false) {
    $state = "<ScaleState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"FFFFFFFF`" ROTATION=`"$(F $rotation)`" ORIGIN.x=`"0`" ORIGIN.y=`"0`" " +
        "POSITION.x=`"$(U16 $x)`" POSITION.y=`"$(U16 $y)`" SCALEX=`"$(F $scaleX)`" SCALEY=`"$(F $scaleY)`" />"
    $body = "<AreaInstance LABEL=`"`" INDEXOFFSET=`"0`"><MATERIALLINK present=`"false`" />" +
        "<LINK slot=`"150`" TIMING=`"$timing`" PACKAGE=`"$package`" AREA=`"$area`" ISUSINGDUPLICATEDAREA=`"false`" /></AreaInstance>"
    Element 33 AreaInstance $name $state $body $hidden
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

# An instance of `area`, `w` by `h`, at (x, y) and tilted `degrees` about its centre; Magma turns an
# instance about its position, so the position is moved to keep the centre in place.
function Tilted([string]$area, [double]$x, [double]$y, [double]$w, [double]$h, [double]$degrees, [bool]$hidden = $false,
                [string]$name = $area) {
    $r = $degrees * [Math]::PI / 180
    $cx = $w / 2 * [Math]::Cos($r) - $h / 2 * [Math]::Sin($r)
    $cy = $w / 2 * [Math]::Sin($r) + $h / 2 * [Math]::Cos($r)
    Instance $area ($x + $w / 2 - $cx) ($y + $h / 2 - $cy) 1 1 $degrees -hidden $hidden -name $name
}

$Exports = [System.Collections.Generic.List[string]]::new()
$NotebookPath = "$PackageId $PageId a_io_notebook a_io_notebook"
$PagePath = "$NotebookPath a_io_page a_io_page"
$PostItPath = "$PagePath a_io_postit a_io_postit"
$ReaderPath = "$PagePath a_io_reader a_io_reader"
# Adds an element to `list` and exports it through `path`: t_io_name as IO_NAME.
function AddExported($list, [string]$path, [string]$element) {
    $list.Add($element)
    $name = [regex]::Match($element, 'USERDATA name="([^"]+)"').Groups[1].Value
    $Exports.Add("<GENERICOBJECT name=`"IO_$($name.Substring(5).ToUpperInvariant())`"><LINK slot=`"69`" LASTOBJECTTYPE=`"Element`" IDS=`"$path $name`" /></GENERICOBJECT>")
}
# An area instanced in the spread's, as each page is.
function AreaPath([string]$area) { "$PagePath $area $area" }

$inkFull = Colour $Ink
$inkSoft = Colour $Ink 0.6
$countInk = Colour $Ink 0.65
$inkFaded = Colour $Ink 0.4
$dimTint = Colour "FFFFFF" 0.4
$redLine = Colour $PencilRed 0.55
$left = 96
$right = 532
$width = 368
# The post-it fills the right page's top, and the right page's lists start below it.
$PostItTop = 56
$Below = $PostItTop + 302 + 16

$page = [System.Collections.Generic.List[string]]::new()
$postIt = [System.Collections.Generic.List[string]]::new()
# Each page's name in the UI's areas and exports, and the label on its tab.
$tabs = [ordered]@{ inventory = "Items"; papers = "Docs"; journal = "Missions"; contacts = "Buddies" }
$areas = [ordered]@{}
foreach ($tab in $tabs.Keys) { $areas[$tab] = [System.Collections.Generic.List[string]]::new() }

# A section's heading, exported when the plugin writes it.
function Heading([string]$tab, [string]$name, [string]$string, [double]$x, [double]$y = 46, [switch]$exported) {
    $heading = Text $name $string $x $y 340 26 24 $inkFull LEFT 0 TOP $false $true
    if ($exported) { AddExported $areas[$tab] (AreaPath "a_io_$tab") $heading } else { $areas[$tab].Add($heading) }
}

# The red pencil line under a row whose text is centred on `middle`, from `x`.
function PencilLine([string]$name, [double]$x, [double]$middle, [double]$lineWidth) {
    Image $name red_line "\common.mgb" $x ($middle - 2) $lineWidth 22 $redLine -0.6
}

# A row's pencil line, shown while the row is selected.
function Selection([string]$tab, [string]$name, [double]$x, [double]$middle, [double]$lineWidth) {
    AddExported $areas[$tab] (AreaPath "a_io_$tab") (PencilLine "i_io_${name}_sel" $x $middle $lineWidth)
}

# The middle of row `j` of a list `step` apart from `top`.
function RowMiddle([double]$top, [double]$step, [int]$j) { $top + ($j - 1) * $step + $step / 2 }

# How many rows are out of a list's window on one side: the pause menu's arrow turned to point
# `rotation` degrees from left, and the count beside it.
function Overflow([string]$tab, [string]$name, [double]$x, [double]$y, [double]$rotation) {
    $path = AreaPath "a_io_$tab"
    AddExported $areas[$tab] $path (Image "i_io_${name}_arrow" pc_arrow "\common.mgb" ($x - 8) ($y - 4) 30 30 $countInk $rotation)
    AddExported $areas[$tab] $path (Text "t_io_$name" "" ($x + 22) $y ($width - 22) 22 18 $countInk)
}

# A list of `lines` rows `step` apart from `top`, each with its selection, a picture of `material`
# if given, its text and a text `side` wide on the right; above and below, how many rows are out of
# the window. With `fades`, each text has a faded italic twin in the same place, for a dead buddy's
# row, and a faded upright one, for a missing buddy's, whose picture has a faded twin too.
function List([string]$tab, [string]$name, [double]$x, [double]$top, [int]$lines, [double]$step,
              [string]$material = "", [double]$pictureW = 0, [double]$pictureH = 0, [string]$tint = "FFFFFFFF",
              [double]$side = 0, [bool]$fades = $false) {
    $area = $areas[$tab]
    $path = AreaPath "a_io_$tab"
    Overflow $tab "${name}s_above" $x ($top - 24) 90
    $textX = $x + $(if ($material) { $pictureW + 10 } else { 0 })
    $textW = $x + $width - 8 - $side - $textX
    $styles = @(@{ Suffix = ""; Ink = $inkFull; SideInk = $inkSoft; Italic = $false })
    if ($fades) {
        $styles += @{ Suffix = "_faded"; Ink = $inkFaded; SideInk = $inkFaded; Italic = $true },
            @{ Suffix = "_dim"; Ink = $inkFaded; SideInk = $inkFaded; Italic = $false }
    }
    for ($j = 1; $j -le $lines; $j++) {
        $middle = RowMiddle $top $step $j
        Selection $tab "$name$j" ($x - 14) $middle ($width + 14)
        if ($material) {
            AddExported $area $path (Image "i_io_$name${j}_pic" $material "" $x ($middle - $pictureH / 2) $pictureW $pictureH $tint)
            if ($fades) {
                AddExported $area $path (Image "i_io_$name${j}_pic_dim" $material "" $x ($middle - $pictureH / 2) $pictureW $pictureH $dimTint)
            }
        }
        foreach ($style in $styles) {
            AddExported $area $path (Text "t_io_$name$j$($style.Suffix)" "" $textX ($middle - 13) $textW 26 22 $style.Ink LEFT 0 CENTER $false $style.Italic)
            if ($side -gt 0) {
                AddExported $area $path (Text "t_io_$name${j}_side$($style.Suffix)" "" ($x + $width - $side) ($middle - 12) $side 24 16 $style.SideInk RIGHT 0 CENTER $false $style.Italic)
            }
        }
    }
    Overflow $tab "${name}s_more" $x ($top + $lines * $step + 2) -90
}

# An index tab: a strip of post-it paper standing up from the spread's top edge, its page's name on it.
function Tab([string]$name, [string]$label, [double]$x, [double]$y, [double]$h, [string]$tint) {
    Image "i_io_$name" postit "\common.mgb" $x $y 104 $h $tint 0 (Crop $TabCrop)
    Text "t_io_${name}_label" $label $x ($y + 4) 104 24 18 $inkFull CENTER
}

# Index tabs along the left page's top, behind the spread: the closed ones grey, the open page's white
# and pulled up.
function AddTabs([string]$look, [double]$y, [double]$h, [string]$tint) {
    $x = 70
    foreach ($tab in $tabs.GetEnumerator()) {
        foreach ($element in Tab "${look}_$($tab.Key)" $tab.Value $x $y $h $tint) { AddExported $page $PagePath $element }
        $x += 106
    }
}

$page.Add((Image i_io_begin io_mark_begin "" 0 0 2 2))
AddTabs tab -16 64 "FFCCCCCC"
AddTabs open -30 74 "FFFFFFFF"
$page.Add((Image i_io_spread $NotebookMaterial "\common.mgb" 0 0 $Spread.W $Spread.H "FFFFFFFF" 0 (Crop $Spread)))

# Items. The left page: Weapons, then Items.
$inventoryArea = $areas.inventory
$InventoryPath = AreaPath a_io_inventory
Heading inventory t_io_weapons "WEAPONS" $left
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    Selection inventory "hand$k" ($left + 24) ($top + 24) ($width - 16)
}
Selection inventory machete ($left + 24) 192 ($width - 16)
# Each gun slot is marked with multiplayer's VIP letter, A or B, tinted with the paper's colour so its
# grey disc takes the paper's tone, as multiplying it would, while its clear corners stay clear; at
# 70% the paper shows through it.
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    $inventoryArea.Add((Image "i_io_hand${k}_slot" "io_slot_$([char](96 + $k))" "" ($left + 2) ($top + 10) 26 26 (Colour "906F4C" 0.7)))
    AddExported $inventoryArea $InventoryPath (Text "t_io_hand${k}_name" "" ($left + 38) ($top + 10) ($width - 46) 26 22 $inkFull)
    AddExported $inventoryArea $InventoryPath (Text "t_io_hand${k}_empty" "Empty" ($left + 38) ($top + 10) ($width - 46) 26 22 $inkSoft)
}
$inventoryArea.Add((Text t_io_machete "Machete" ($left + 38) 178 ($width - 46) 26 22 $inkFull))

# Items shows a window of ten rows that follows the selection.
$ItemsTop = 276
$ItemLines = 10
$ItemStep = 32
Heading inventory t_io_items "ITEMS" $left 226
List inventory item $left $ItemsTop $ItemLines $ItemStep

# A stowed gun's context menu: a scrap of post-it right of its row, past the longest gun name, with the
# slots the gun can go to, its first on the row's line, each with its pencil line. One area, instanced
# beside every row of the window, all hidden; the instances share it, so its lines are exported once.
$MenuW = 190
$MenuH = 90
$MenuFirst = ($MenuH - $ItemStep) / 2
$menuActions = "To slot A", "To slot B"
$menu = [System.Collections.Generic.List[string]]::new()
$menu.Add((Image i_io_menu_paper postit "\common.mgb" 0 0 $MenuW $MenuH "FFFFFFFF" 0 (Crop $PostItCrop)))
for ($k = 0; $k -lt $menuActions.Count; $k++) {
    $middle = $MenuFirst + $k * $ItemStep
    AddExported $menu "$InventoryPath a_io_menu1 a_io_menu" (PencilLine "i_io_menu_sel$k" 8 $middle ($MenuW - 18))
    $menu.Add((Text "t_io_menu$k" $menuActions[$k] 22 ($middle - 13) ($MenuW - 34) 26 22 $inkFull))
}
for ($j = 1; $j -le $ItemLines; $j++) {
    $middle = RowMiddle $ItemsTop $ItemStep $j
    AddExported $inventoryArea $InventoryPath (Tilted a_io_menu ($left + 165) ($middle - $MenuFirst) $MenuW $MenuH -0.8 $true "a_io_menu$j")
}

# The right page, below the post-it: the belt.
Heading inventory t_io_belt "BELT" $right $Below
$belt = @(
    @{ Name = "Syrettes"; Pic = "seringe"; Material = "hud_icon_seringe"; Blending = "Normal" },
    @{ Name = "Malaria pills"; Pic = "pills"; Material = "hud_icon_pills"; Blending = "Normal" },
    @{ Name = "M67 grenades"; Pic = "m67"; Material = "io_gun_m67"; Blending = "Multiply" },
    @{ Name = "Molotovs"; Pic = "molotov"; Material = "io_gun_molotov"; Blending = "Multiply" })
for ($j = 1; $j -le $belt.Count; $j++) {
    $top = $Below + 30 + ($j - 1) * 40
    Selection inventory "belt$j" ($right - 14) ($top + 20) ($width + 14)
    $inventoryArea.Add((Image "i_io_belt$j" $belt[$j - 1].Material "" $right ($top + 5) 30 30 "FFFFFFFF" 0 @(0, 0, 1, 1) $belt[$j - 1].Blending))
    $inventoryArea.Add((Text "t_io_belt$j" $belt[$j - 1].Name ($right + 40) ($top + 7) 260 26 22 $inkFull))
    AddExported $inventoryArea $InventoryPath (Text "t_io_belt${j}_count" "" ($right + 300) ($top + 7) 76 26 20 $inkFull RIGHT)
}

# Docs: the documents on the left, the Jackal and Predecessor tapes on the right below the
# post-it.
Heading papers t_io_letters "DOCUMENTS" $left
List papers letter $left 98 10 40
Heading papers t_io_tapes "JACKAL$([char]0x2019)S TAPES" $right $Below
List papers tape $right ($Below + 52) 4 40 io_tape 40 20 (Colour "000000" 0.8)

# Missions: the reputation in the game's words on the left; on the right the current job, its
# objectives and the Underground's, as the pause menu's main page showed them.
$JournalPath = AreaPath a_io_journal
Heading journal t_io_reputation "" $left -exported
AddExported $areas.journal $JournalPath (Text t_io_reputation_prose "" $left 80 $width 400 22 $inkFull LEFT 0 TOP $true)
Heading journal t_io_jobs "JOB" $right
AddExported $areas.journal $JournalPath (Text t_io_job "" $right 80 $width 120 22 $inkFull LEFT 0 TOP $true)
AddExported $areas.journal $JournalPath (Text t_io_jobs_none "No job right now." $right 80 $width 22 18 $countInk)
Heading journal t_io_objectives "OBJECTIVES" $right 216 -exported
foreach ($k in 1, 2) {
    AddExported $areas.journal $JournalPath (Text "t_io_objective$k" "" $right (252 + ($k - 1) * 68) $width 64 20 $inkFull LEFT 0 TOP $true)
}
Heading journal t_io_underground "UNDERGROUND" $right 400 -exported
AddExported $areas.journal $JournalPath (Text t_io_underground_job "" $right 436 $width 88 20 $inkFull LEFT 0 TOP $true)

# Buddies, on the left page: the primary and secondary buddy, then the other buddies, each with a
# portrait the plugin takes from a_io_portraits and a status.
Heading contacts t_io_actives "ACTIVE BUDDIES" $left
List contacts active $left 98 2 50 io_buddy_frank 44 44 "FFFFFFFF" 130 $true
Heading contacts t_io_others "OTHER BUDDIES" $left 236
List contacts other $left 288 6 50 io_buddy_frank 44 44 "FFFFFFFF" 130 $true
$palette = [System.Collections.Generic.List[string]]::new()
foreach ($portrait in $Portraits) {
    AddExported $palette (AreaPath a_io_portraits) (Image "i_io_portrait_$portrait" "io_buddy_$portrait" "" 0 0 44 44)
}

foreach ($tabName in $areas.Keys) {
    AddExported $page $PagePath (Instance "a_io_$tabName" 0 0 1 1 0)
}
AddExported $page $PagePath (Instance a_io_portraits 0 0 1 1 0)

# The papers' pictures, which the plugin copies onto the post-it and the reader: each mod adds its own
# to this area, an image named after its paper's Items id, dots made underscores, and exports it as
# IO_PAPER_<ID>.
$paperPictures = [System.Collections.Generic.List[string]]::new()
# The example photo's picture.
AddExported $paperPictures (AreaPath a_io_paper_pictures) (Image i_io_paper_photo_example_photo io_paper_photo_example_photo "" 0 0 44 44)
AddExported $page $PagePath (Instance a_io_paper_pictures 0 0 1 1 0)

# The post-it, in its own pixels; its instance tilts it about its centre.
$postIt.Add((Image i_io_postit_paper postit "\common.mgb" 0 0 416 302 "FFFFFFFF" 0 (Crop $PostItCrop)))
# The paperclip lies on its side; it is turned a quarter clockwise to stand across the post-it's top.
$postIt.Add((Image i_io_postit_clip paperclip "\common.mgb" (300 - 48) (6 - 24) 96 48 "FFFFFFFF" 90))
# The wide pictures, the guns' and the machete's, are kept small enough not to look blocky.
foreach ($pic in $GunPictures) {
    AddExported $postIt $PostItPath (Image "i_io_postit_pic_$pic" "io_gun_$pic" "" 94 29 230 58 "FFFFFFFF" 0 @(0, 0, 1, 1) Multiply)
}
AddExported $postIt $PostItPath (Image i_io_postit_pic_machete hud_icon_machete "" 94 29 230 58)
AddExported $postIt $PostItPath (Image i_io_postit_pic_mortar hud_icon_mortar_shop "" 94 29 230 58 (Colour "000000" 0.8))
# The wallet's and the belt's pictures are square.
foreach ($square in @(@{ Pic = "diamond"; Material = "ui_hud_icon_diamond"; Blending = "Normal" }) + $belt) {
    AddExported $postIt $PostItPath (Image "i_io_postit_pic_$($square.Pic)" $square.Material "" 173 22 72 72 "FFFFFFFF" 0 @(0, 0, 1, 1) $square.Blending)
}
# A thing with no picture of its own has the malaria mosquito, unscaled in the same square, its white
# multiplied away.
AddExported $postIt $PostItPath (Image i_io_postit_pic_placeholder io_placeholder "" 177 26 64 64 "FFFFFFFF" 0 @(0, 0, 1, 1) Multiply)
# Where the post-it shows a paper's picture, which the plugin copies in; every picture is square.
AddExported $postIt $PostItPath (Image i_io_postit_pic_paper io_mark_end "" 160 2 94 94)
AddExported $postIt $PostItPath (Image i_io_postit_pic_tape io_tape "" 160 26 96 48 (Colour "000000" 0.8))
AddExported $postIt $PostItPath (Image i_io_postit_pic_envelope io_envelope "" 177 26 64 64)
AddExported $postIt $PostItPath (Image i_io_postit_portrait io_buddy_frank "" 172 16 72 72)
AddExported $postIt $PostItPath (Text t_io_postit_name "" 26 96 366 26 24 $inkFull)
AddExported $postIt $PostItPath (Text t_io_postit_sub "" 26 124 366 18 15 $inkSoft)
# What a thing with no subtitle is, wrapped right under its name.
AddExported $postIt $PostItPath (Text t_io_postit_about "" 26 128 366 110 18 $inkFull LEFT 0 TOP $true)
# The post-it's body, everything below the subtitle, an area instanced twice: in its place, and
# raised into the subtitle's line for a post-it with none. The two share the area, so its elements are
# exported once, through the first.
$postItBody = [System.Collections.Generic.List[string]]::new()
$BodyPath = "$PostItPath a_io_postit_body a_io_postit_body"
$BodyRaise = 13
# Three lines of a label and a value: a gun's condition and ammo, a belt item's count, then the
# upgrades bought for it.
for ($l = 1; $l -le 3; $l++) {
    AddExported $postItBody $BodyPath (Text "t_io_postit_line$l" "" 26 (148 + ($l - 1) * 26) 120 24 20 $inkFull)
}
AddExported $postItBody $BodyPath (Text t_io_postit_value1 "" 101 148 100 24 20 $inkFull RIGHT)
AddExported $postItBody $BodyPath (Text t_io_postit_value2 "" 81 174 120 24 20 $inkFull RIGHT)
# A paper's or a tape's first line, a label and a longer value, and what it says below; a buddy's
# status, at the post-it's right edge.
AddExported $postItBody $BodyPath (Text t_io_postit_state "" 140 148 252 24 20 $inkFull)
AddExported $postItBody $BodyPath (Text t_io_postit_status "" 140 148 252 24 20 $inkFull RIGHT)
AddExported $postItBody $BodyPath (Text t_io_postit_quote "" 26 176 366 84 17 $inkFull LEFT 0 TOP $true)
# The game's upgrade icons, faint until bought, in a row after a line's label: a gun's operation and
# repair manuals and its bandolier on the third line, under its condition and ammo; a belt item's
# first aid manuals or bandolier on the second, under its count.
function Upgrades([string]$row, [string[]]$icons, [double]$y) {
    for ($u = 0; $u -lt $icons.Count; $u++) {
        $material = "io_upgrade_$($icons[$u])"
        AddExported $postItBody $BodyPath (Image "i_io_postit_${row}${u}_faint" $material "" (125 + $u * 27) $y 22 22 (Colour $Ink 0.35))
        AddExported $postItBody $BodyPath (Image "i_io_postit_${row}${u}_bought" $material "" (125 + $u * 27) $y 22 22 (Colour $Ink 0.85))
    }
}
Upgrades up "manual", "repair", "ammo" 201
Upgrades belt_up "ammo", "ammo" 175
AddExported $postItBody $BodyPath (Text t_io_postit_note "" 26 236 366 22 18 $inkFull)
# Each rating is a brush stroke as long as the rating, the way the loadout draws them, over a faint
# full-length one: one stroke per half point, all but one hidden.
for ($r = 1; $r -le $Ratings.Count; $r++) {
    $top = 148 + ($r - 1) * 25
    AddExported $postItBody $BodyPath (Text "t_io_postit_rating$r" $Ratings[$r - 1] 217 $top 110 22 14 $inkFull)
    AddExported $postItBody $BodyPath (Image "i_io_postit_rating${r}_track" brush_stroke "\common.mgb" 330 ($top + 4) 58 14 (Colour $Ink 0.18))
    for ($half = 1; $half -le 10; $half++) {
        AddExported $postItBody $BodyPath (Image "i_io_postit_rating${r}_$half" brush_stroke "\common.mgb" 330 ($top + 4) (5.8 * $half) 14 (Colour $Ink 0.85) 0 @(0, 0, ($half / 10), 1))
    }
}
AddExported $postIt $PostItPath (Instance a_io_postit_body 0 0 1 1 0)
AddExported $postIt $PostItPath (Instance a_io_postit_body 0 (-$BodyRaise) 1 1 0 a_io_postit_body_up -hidden $true)
# The post-it's place at the right page's top, tilted about its centre, in the page's own pixels.
AddExported $page $PagePath (Tilted a_io_postit 512 $PostItTop 416 302 -1.4)

# The paper being read, over the whole right page on the post-it's paper turned upright: its picture on
# the first sheet with the sheet's text below it, or the sheet's text alone, and which sheet of how many.
$reader = [System.Collections.Generic.List[string]]::new()
$reader.Add((Image i_io_reader_paper postit "\common.mgb" -104 104 620 412 "FFFFFFFF" 90 (Crop $PostItCrop)))
AddExported $reader $ReaderPath (Image i_io_reader_pic io_mark_end "" 16 16 380 380)
AddExported $reader $ReaderPath (Text t_io_reader_text "" 30 30 352 548 24 $inkFull LEFT 0 TOP $true -hand $true)
AddExported $reader $ReaderPath (Text t_io_reader_caption "" 30 404 352 174 24 $inkFull LEFT 0 TOP $true -hand $true)
AddExported $reader $ReaderPath (Text t_io_reader_sheet "" 30 584 352 20 15 $inkSoft RIGHT)
AddExported $page $PagePath (Tilted a_io_reader 509 38 412 620 1.2 $true)
# The pause menu's own page flip, common.mgb's area, which the plugin steps through itself. The pause
# pages turn forward with it at (-27, -24), 1.0390625 as tall, over notebook.png drawn 1097 by 982
# from (-58, -1); that carried into the spread's pixels, which draw notebook.png unscaled from
# (4, 10). Shown only while R turns a page.
AddExported $page $PagePath (Instance "#5B36589B" ((-27 + 58) / (1097 / 1024) - $Spread.X) ((-24 + 1) / (982 / 1024) - $Spread.Y) `
    (1 / (1097 / 1024)) (1.0390625 / (982 / 1024)) 0 a_io_flip $CommonId TickTimingStrategy $true)
$Exports.Add("<GENERICOBJECT name=`"IO_FLIP_AREA`"><LINK slot=`"44`" LASTOBJECTTYPE=`"Area`" IDS=`"$PagePath a_io_flip #5B36589B`" /></GENERICOBJECT>")
# The page's last image.
$page.Add((Image i_io_end io_mark_end "" 0 0 2 2))

function Material([string]$name, [string]$texture) {
    "<Material name=`"$name`" texture=`"$texture`" REGION=`"0 0 1 1`" />"
}
$materials = @(
    ($GunPictures + "m67", "molotov") | ForEach-Object { Material "io_gun_$_" "\textures\guns\gun_icon_$_.png" }
    ("manual", "repair", "ammo") | ForEach-Object { Material "io_upgrade_$_" "\textures\ui_icon_$($_)upgrade.png" }
    $Portraits | ForEach-Object { Material "io_buddy_$_" "\textures\hud\icons_buddies\buddies_$_.png" }
    Material io_paper_photo_example_photo "\textures\io\example_photo.png"
    Material io_tape "\textures\ui_tape_icon.png"
    Material io_envelope "\textures\hud\icons_objectives\icon_partner_mission.png"
    Material io_placeholder "\textures\common\mosquito.png"
    ("a", "b") | ForEach-Object { Material "io_slot_$_" "\textures\hud_mp\vip_$_.png" }
    Material io_mark_begin "\textures\io\mark_begin.png"
    Material io_mark_end "\textures\io\mark_end.png") -join "`n"

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

Remove-Item -Recurse -Force $Work, $Textures -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Work, $Textures | Out-Null

# The markers are clear, single-level DXT5 under the header of a stock HUD texture that has no _mip0
# companion.
Exec $Cli xbt extract $StockTexture -o $Work | Out-Null
Add-Type -AssemblyName System.Drawing
# Their sizes are how the plugin knows them.
foreach ($mark in @(@{ Name = "mark_begin"; W = 4; H = 64 }, @{ Name = "mark_end"; W = 64; H = 4 })) {
    $clear = New-Object System.Drawing.Bitmap $mark.W, $mark.H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $clear.Save("$Work\$($mark.Name).png", [System.Drawing.Imaging.ImageFormat]::Png)
    $clear.Dispose()
    Exec $Texconv -nologo -f DXT5 -m 1 -y -o $Work "$Work\$($mark.Name).png" | Out-Null
    Exec $Cli xbt build "$Work\$($mark.Name).dds" "$Work\ui_tape_icon.xml" -o "$Textures\$($mark.Name).xbt" | Out-Null
}
# Any image fitted whole into a square tiff, clear around it, for a paper's picture. Not a png: .NET tags
# those sRGB, and texconv then darkens them.
function SquarePicture([string]$source, [string]$target, [int]$size) {
    $image = [System.Drawing.Image]::FromFile($source)
    $scale = $size / [Math]::Max($image.Width, $image.Height)
    $w = [int]($image.Width * $scale)
    $h = [int]($image.Height * $scale)
    $square = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($square)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImage($image, [int](($size - $w) / 2), [int](($size - $h) / 2), $w, $h)
    $square.Save($target, [System.Drawing.Imaging.ImageFormat]::Tiff)
    $graphics.Dispose()
    $square.Dispose()
    $image.Dispose()
}
# The example photo, under the same header.
SquarePicture "$PSScriptRoot\assets\photo.png" "$Work\photo.tif" 512
Exec $Texconv -nologo -f DXT5 -m 1 -y -o $Work "$Work\photo.tif" | Out-Null
Exec $Cli xbt build "$Work\photo.dds" "$Work\ui_tape_icon.xml" -o "$Textures\example_photo.xbt" | Out-Null

# Caveat drawn into ui\fonts, with its font and family in every fonts.mgb and its files in every UI
# dependency list, which is what the game preloads, as fragments of the game's own.
Remove-Item -Recurse -Force "$PSScriptRoot\layer\mods\ui\fonts", "$Layer\*\*\ui\fonts.mgb", "$Layer\*\*\ui\_depload.xml" `
    -ErrorAction SilentlyContinue
Exec $Cli mft build "$PSScriptRoot\notebook\fonts\Caveat-VariableFont_wght.ttf" --axis "wght=$CaveatWeight" `
    --size $CaveatSize --no-cyrillic --name $Caveat --out "$PSScriptRoot\layer" --game $Game | Out-Null

# A changed package's XML checked and written into the layer as fragments of its retail package.
function Ship([System.Collections.Generic.List[string]]$lines, [string]$xml, [System.IO.FileInfo]$source,
              [string]$variant) {
    [IO.File]::WriteAllLines($xml, $lines)
    Exec $Cli mgb verify $xml
    $target = Join-Path $Layer "$variant\ui\$($source.Name)"
    Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
    Exec $Cli mgb fragments $xml --base $source.FullName --out $target | Out-Null
}

foreach ($source in Get-ChildItem -Recurse -Path $Retail -Filter hud.mgb) {
    $variant = $source.Directory.Parent.FullName.Substring($Retail.Length + 1)
    $dir = Join-Path $Work $variant
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Exec $Cli mgb decode $source.FullName -o "$dir\hud.xml" | Out-Null
    $lines = [System.Collections.Generic.List[string]]::new([IO.File]::ReadAllLines("$dir\hud.xml"))

    # The canvas page, and the sheet over the whole of it.
    $header = $lines[0]
    $screenX = -(Attribute $header 'DISPLAYOFFSET.x')
    $screenY = -(Attribute $header 'DISPLAYOFFSET.y')
    $screenW = Attribute $header 'PAGESIZE.w'
    $screenH = Attribute $header 'PAGESIZE.h'
    $scaleX = $screenW / $Spread.W
    $scaleY = $screenH / ($Spread.H - $SheetTop)
    $y = $screenY - $SheetTop * $scaleY

    $areaXml = @(
        (Area a_io_notebook $Spread.W $Spread.H @(Instance a_io_page 0 0 1 1 0))
        (Area a_io_page $Spread.W $Spread.H $page)
        foreach ($tabName in $areas.Keys) { Area "a_io_$tabName" $Spread.W $Spread.H $areas[$tabName] }
        (Area a_io_portraits 44 44 $palette)
        (Area a_io_paper_pictures 44 44 $paperPictures)
        (Area a_io_postit 416 302 $postIt)
        (Area a_io_postit_body 416 302 $postItBody)
        (Area a_io_reader 412 620 $reader)
        (Area a_io_menu $MenuW $MenuH $menu)) -join "`n"
    $objects = @("<GENERICOBJECT name=`"IO_NOTEBOOK`"><LINK slot=`"69`" LASTOBJECTTYPE=`"Element`" IDS=`"$NotebookPath a_io_page`" /></GENERICOBJECT>") + $Exports

    # Generic objects, the areas, the page, then materials: back to front, so each insertion leaves
    # the earlier indices valid.
    $at = IndexOf $lines '<GENERICOBJECTS>'
    $lines.Insert($at + 1, ($objects -join "`n"))

    $strings = IndexOf $lines '<STRINGTABLE'
    $lines.Insert($strings - 1, $areaXml)

    # Last on the HUD page, so its draws are the last of the HUD's.
    $pageEnd = IndexOf $lines '</CHILDREN>' (IndexOf $lines "<USERDATA name=`"$PageId`">")
    $lines.Insert($pageEnd, (Instance a_io_notebook $screenX $y $scaleX $scaleY 0))

    $materialsAt = IndexOf $lines '<MATERIALS materialExtra='
    $lines.Insert($materialsAt + 1, $materials)

    Ship $lines "$dir\hud.xml" $source $variant
    Write-Host ("{0,-20} notebook at {1:0},{2:0} scaled {3:0.000} x {4:0.000}" -f $variant, $screenX, $y, $scaleX, $scaleY)
}

# The first and last line of the element named `name` after line `from`: its <Element> and </Element>.
function ElementRange([System.Collections.Generic.List[string]]$lines, [string]$name, [int]$from) {
    $first = (IndexOf $lines "<USERDATA name=`"$name`">" $from) - 1
    $indent = $lines[$first].Substring(0, $lines[$first].IndexOf('<'))
    , @($first, (IndexOf $lines "$indent</Element>" $first))
}

function ElementLines([System.Collections.Generic.List[string]]$lines, [string]$name, [int]$from) {
    $range = ElementRange $lines $name $from
    , $lines.GetRange($range[0], $range[1] - $range[0] + 1).ToArray()
}

# An instance's position in its last keyframe.
function LastPosition([string[]]$element) {
    $last = @($element -match 'POSITION\.x=')[-1]
    , @((Attribute $last 'POSITION.x'), (Attribute $last 'POSITION.y'))
}

# The pause menu's main page as the game-over dialog, common.mgb's message box list page: its dimming
# and post-it over the menu's clouds, the menu's list where the dialog's is, and the rest hidden, the
# page flip it opens with among them.
$PausePage = "#C1849B53"
$PauseHidden = "#E039A398", "#36D6DAD3", "#95871E12", "#81FDCEB5", "#B638527D", "#6CF418D5", "#C801AB46",
    "#684AC59C", "#F0CC8C29", "#EDE19BDA", "#2A5FDD43", "#C54DB506", "#B1CC4148", "#5B36589B"
$PausePackage = "#7B435D46"
$PauseClouds = "#B21251FF"
$PauseList = "#3D23C3C5"
$DialogPage = "#D24218AB"
$DialogDim = "#E1CD80CB"
$DialogPostIt = "#815EB148"
$DialogPostItArea = "#D096B132"
$DialogPostItPaper = "#06E8B233"
# The list in the dialog's page, and in the list panel the pause page instances, common.mgb's #9EA91A65.
$CommonList = "#0D9428EC"
$ListPanel = "#9EA91A65"
foreach ($source in Get-ChildItem -Recurse -Path $Retail -Filter sp_pause_menus.mgb) {
    $variant = $source.Directory.Parent.FullName.Substring($Retail.Length + 1)
    $dir = Join-Path $Work $variant
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Exec $Cli mgb decode $source.FullName -o "$dir\sp_pause_menus.xml" | Out-Null
    Exec $Cli mgb decode (Join-Path $source.DirectoryName common.mgb) -o "$dir\common.xml" | Out-Null
    $common = [System.Collections.Generic.List[string]]::new([IO.File]::ReadAllLines("$dir\common.xml"))
    $dialog = IndexOf $common "<USERDATA name=`"$DialogPage`">"
    $dim = (ElementLines $common $DialogDim $dialog) -replace "name=`"$DialogDim`"", 'name="r_io_pause_dim"'
    $postIt = [System.Collections.Generic.List[string]]::new([string[]]((ElementLines $common $DialogPostIt $dialog) `
        -replace "name=`"$DialogPostIt`"", 'name="a_io_pause_postit"' `
        -replace "PACKAGE=`"$CommonId`" AREA=`"$DialogPostItArea`"", "PACKAGE=`"$PausePackage`" AREA=`"a_io_pause_postit`""))
    # Its last keyframe alone, from the first frame: the dialog's post-it pops in, and the list is there at once.
    $keys = IndexOf $postIt '<KEYFRAMES>'
    $last = $postIt.FindLastIndex([Predicate[string]] { param($line) $line.Contains('<Keyframe ') })
    $postIt.RemoveRange($keys + 1, $last - $keys - 1)
    $postIt[$keys + 1] = $postIt[$keys + 1] -replace 'IDX="\d+"', 'IDX="0"'
    $postItArea = IndexOf $common "<USERDATA name=`"$DialogPostItArea`">"
    $postItBox = [regex]::Match($common[$postItArea - 1], 'STATICBOX="0 (\d+) 0 (\d+)"').Groups
    $paper = (ElementLines $common $DialogPostItPaper $postItArea) `
        -replace "name=`"$DialogPostItPaper`"", 'name="i_io_pause_postit"' -replace 'id="postit" PACKAGE=""', 'id="postit" PACKAGE="\common.mgb"'
    $listAt = LastPosition (ElementLines $common $CommonList $dialog)
    $inPanel = LastPosition (ElementLines $common $CommonList (IndexOf $common "<USERDATA name=`"$ListPanel`">"))

    $lines = [System.Collections.Generic.List[string]]::new([IO.File]::ReadAllLines("$dir\sp_pause_menus.xml"))
    $start = IndexOf $lines "<USERDATA name=`"$PausePage`">"
    $end = IndexOf $lines '</Area>' $start
    foreach ($element in $PauseHidden) {
        $at = IndexOf $lines "<USERDATA name=`"$element`">" $start
        if ($at -gt $end) { throw "$element is not on the pause page in $variant" }
        $lines[$at - 1] = $lines[$at - 1].Replace('HIDDEN="false"', 'HIDDEN="true"')
    }
    # The list panel where the dialog has its list, unscaled.
    $list = ElementRange $lines $PauseList $start
    for ($i = $list[0]; $i -lt $list[1]; $i++) {
        $lines[$i] = $lines[$i] -replace 'POSITION\.x="\d+" POSITION\.y="\d+" SCALEX="[\d.]+" SCALEY="[\d.]+"',
            "POSITION.x=`"$(U16 ($listAt[0] - $inPanel[0]))`" POSITION.y=`"$(U16 ($listAt[1] - $inPanel[1]))`" SCALEX=`"1`" SCALEY=`"1`""
    }
    $lines.InsertRange($list[0], [string[]]$postIt)
    $lines.InsertRange((ElementRange $lines $PauseClouds $start)[0], [string[]]$dim)
    $lines.Insert((IndexOf $lines '<STRINGTABLE') - 1, (Area a_io_pause_postit $postItBox[1].Value $postItBox[2].Value $paper))
    Ship $lines "$dir\sp_pause_menus.xml" $source $variant
}
