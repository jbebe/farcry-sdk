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
    a_io_portraits the buddies' portraits a row takes its picture from, and a_io_postit the post-it
    stuck on it. Everything the plugin fills or hides is exported as IO_*; a gun's picture is one image
    per gun and slot, all but one hidden.

    Run from anywhere; reads the retail files under tmp\, writes layer\mods\ui.
#>
$ErrorActionPreference = "Stop"

$Repo = Resolve-Path "$PSScriptRoot\..\.."
$Cli = "$Repo\tools\JackAll\src\JackAll.Cli\bin\Release\net10.0\jackall-cli.exe"
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
# The sample papers' pictures, in textures\common, and the buddies' portraits,
# textures\hud\icons_buddies\buddies_<name>.png.
$PaperPictures = "jackal_photo", "jackal_definition", "apr_01", "ufll_01"
# The papers' photos, loading-screen pictures by their number in textures\loading, each kept by the
# game as six tiles of a 1280 by 800 image: x, y, width and height.
$Photos = "05"
$PhotoTiles = @(0, 0, 256, 512), @(256, 0, 1024, 512), @(0, 512, 256, 256), @(256, 512, 1024, 256), @(0, 768, 256, 32),
    @(256, 768, 1024, 32)
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
# ellipsis; or, with `wrap`, prose wrapped from the top of its box.
function Text([string]$name, [string]$string, [double]$x, [double]$y, [double]$w, [double]$h, [int]$height,
              [string]$colour, [string]$align = "LEFT", [double]$rotation = 0, [string]$alignY = "CENTER",
              [bool]$wrap = $false, [bool]$italic = $false) {
    $tracking = if ($height -ge 22) { 0 } elseif ($height -ge 19) { 65516 } else { 65521 }
    $wrapping = $wrap.ToString().ToLowerInvariant()
    $ellipsis = (-not $wrap).ToString().ToLowerInvariant()
    $italics = $italic.ToString().ToLowerInvariant()
    $state = "<TextState INTERPOLATIONFLAGS=`"0`" STATECOLOR=`"$colour`" $(Placement $x $y $w $h $rotation) " +
        "OFFSETY=`"0`" ABSOFFSETY=`"0`" SHADOWCOLOR=`"FF000000`" HEIGHT=`"$height`" SHADOWOFFSETX=`"0`" SHADOWOFFSETY=`"0`" " +
        "LEADING=`"$height`" TRACKING=`"$tracking`" />"
    $body = "<Text useStringTable=`"false`" STRING=`"$string`" ALIGNMENTX=`"$align`" ALIGNMENTY=`"$alignY`" WRAPPING=`"$wrapping`" " +
        "CLIPPING=`"false`" ELLIPSIS=`"$ellipsis`" AUTOSIZED=`"false`" BOLD=`"false`" ITALICS=`"$italics`" UNDERLINED=`"false`" " +
        "BLENDINGMODE=`"Normal`" ALPHABLENDFIRST=`"false`"><FONTFAMILY present=`"true`" id=`"$Farcry2`" PACKAGE=`"\fonts.mgb`" /></Text>"
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
function Tilted([string]$area, [double]$x, [double]$y, [double]$w, [double]$h, [double]$degrees, [bool]$hidden = $false) {
    $r = $degrees * [Math]::PI / 180
    $cx = $w / 2 * [Math]::Cos($r) - $h / 2 * [Math]::Sin($r)
    $cy = $w / 2 * [Math]::Sin($r) + $h / 2 * [Math]::Cos($r)
    Instance $area ($x + $w / 2 - $cx) ($y + $h / 2 - $cy) 1 1 $degrees -hidden $hidden
}

$Exports = [System.Collections.Generic.List[string]]::new()
$NotebookPath = "$PackageId $PageId a_io_notebook a_io_notebook"
$PagePath = "$NotebookPath a_io_page a_io_page"
$PostItPath = "$PagePath a_io_postit a_io_postit"
$ReaderPath = "$PagePath a_io_reader a_io_reader"
function Export([string]$path, [string]$element) {
    $Exports.Add("<GENERICOBJECT name=`"IO_$($element.Substring(5).ToUpperInvariant())`"><LINK slot=`"69`" LASTOBJECTTYPE=`"Element`" IDS=`"$path $element`" /></GENERICOBJECT>")
}
# An area instanced in the spread's, as each page is.
function AreaPath([string]$area) { "$PagePath $area $area" }

$inkFull = Colour $Ink
$inkSoft = Colour $Ink 0.6
$countInk = Colour $Ink 0.65
$inkFaded = Colour $Ink 0.4
$redLine = Colour $PencilRed 0.55
$left = 96
$right = 532
$width = 368
# The post-it fills the right page's top, and the right page's lists start below it.
$PostItTop = 56
$Below = $PostItTop + 302 + 16

$page = [System.Collections.Generic.List[string]]::new()
$postIt = [System.Collections.Generic.List[string]]::new()
$tabs = "Inventory", "Papers", "Journal", "Contacts"
$areas = [ordered]@{}
foreach ($tab in $tabs) { $areas[$tab.ToLowerInvariant()] = [System.Collections.Generic.List[string]]::new() }

# A section's heading.
function Heading([string]$tab, [string]$name, [string]$string, [double]$x, [double]$y = 46) {
    $areas[$tab].Add((Text $name $string $x $y 340 26 24 $inkFull LEFT 0 TOP $false $true))
}

# A row's red pencil line from `x`, shown while the row whose text is centred on `middle` is selected.
function Selection([string]$tab, [string]$name, [double]$x, [double]$middle, [double]$lineWidth) {
    $areas[$tab].Add((Image "i_io_${name}_sel" red_line "\common.mgb" $x ($middle - 2) $lineWidth 22 $redLine -0.6))
    Export (AreaPath "a_io_$tab") "i_io_${name}_sel"
}

# How many rows are out of a list's window on one side: the pause menu's arrow turned to point
# `rotation` degrees from left, and the count beside it.
function Overflow([string]$tab, [string]$name, [double]$x, [double]$y, [double]$rotation) {
    $path = AreaPath "a_io_$tab"
    $areas[$tab].Add((Image "i_io_${name}_arrow" pc_arrow "\common.mgb" ($x - 8) ($y - 4) 30 30 $countInk $rotation))
    $areas[$tab].Add((Text "t_io_$name" "" ($x + 22) $y ($width - 22) 22 18 $countInk))
    Export $path "i_io_${name}_arrow"
    Export $path "t_io_$name"
}

# A list of `lines` rows `step` apart from `top`, each with its selection, a picture of `material`
# if given, its text and a text `side` wide on the right; above and below, how many rows are out of
# the window. With `fades`, each text has a faded italic twin in the same place, for a row the
# plugin greys out.
function List([string]$tab, [string]$name, [double]$x, [double]$top, [int]$lines, [double]$step,
              [string]$material = "", [double]$pictureW = 0, [double]$pictureH = 0, [string]$tint = "FFFFFFFF",
              [double]$side = 0, [bool]$fades = $false) {
    $area = $areas[$tab]
    $path = AreaPath "a_io_$tab"
    Overflow $tab "${name}s_above" $x ($top - 24) 90
    $textX = $x + $(if ($material) { $pictureW + 10 } else { 0 })
    for ($j = 1; $j -le $lines; $j++) {
        $middle = $top + ($j - 1) * $step + $step / 2
        Selection $tab "$name$j" ($x - 14) $middle ($width + 14)
        if ($material) {
            $area.Add((Image "i_io_$name${j}_pic" $material "" $x ($middle - $pictureH / 2) $pictureW $pictureH $tint))
            Export $path "i_io_$name${j}_pic"
        }
        $area.Add((Text "t_io_$name$j" "" $textX ($middle - 13) ($x + $width - 8 - $side - $textX) 26 22 $inkFull))
        Export $path "t_io_$name$j"
        if ($fades) {
            $area.Add((Text "t_io_$name${j}_faded" "" $textX ($middle - 13) ($x + $width - 8 - $side - $textX) 26 22 $inkFaded LEFT 0 CENTER $false $true))
            Export $path "t_io_$name${j}_faded"
        }
        if ($side -gt 0) {
            $area.Add((Text "t_io_$name${j}_side" "" ($x + $width - $side) ($middle - 12) $side 24 16 $inkSoft RIGHT))
            Export $path "t_io_$name${j}_side"
            if ($fades) {
                $area.Add((Text "t_io_$name${j}_side_faded" "" ($x + $width - $side) ($middle - 12) $side 24 16 $inkFaded RIGHT 0 CENTER $false $true))
                Export $path "t_io_$name${j}_side_faded"
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
    for ($i = 0; $i -lt $tabs.Count; $i++) {
        $name = "${look}_$($tabs[$i].ToLowerInvariant())"
        $page.AddRange([string[]](Tab $name $tabs[$i] (70 + $i * 106) $y $h $tint))
        Export $PagePath "i_io_$name"
        Export $PagePath "t_io_${name}_label"
    }
}

$page.Add((Image i_io_begin io_mark_begin "" 0 0 2 2))
AddTabs tab -16 64 "FFCCCCCC"
AddTabs open -30 74 "FFFFFFFF"
$page.Add((Image i_io_spread $NotebookMaterial "\common.mgb" 0 0 $Spread.W $Spread.H "FFFFFFFF" 0 (Crop $Spread)))

# Inventory. The left page: Weapons, then Items.
$inventoryArea = $areas.inventory
$InventoryPath = AreaPath a_io_inventory
Heading inventory t_io_weapons "WEAPONS" $left
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    $inventoryArea.Add((Image "i_io_hand${k}_sel" red_line "\common.mgb" ($left + 24) ($top + 22) ($width - 16) 22 $redLine -0.6))
    Export $InventoryPath "i_io_hand${k}_sel"
}
Selection inventory machete ($left + 24) 192 ($width - 16)
# Each gun slot is marked with multiplayer's VIP letter, A or B, tinted with the paper's colour so its
# grey disc takes the paper's tone, as multiplying it would, while its clear corners stay clear; at
# 70% the paper shows through it.
foreach ($k in 1, 2) {
    $top = 76 + ($k - 1) * 46
    $inventoryArea.Add((Image "i_io_hand${k}_slot" "io_slot_$([char](96 + $k))" "" ($left + 2) ($top + 10) 26 26 (Colour "906F4C" 0.7)))
    $inventoryArea.Add((Text "t_io_hand${k}_name" "" ($left + 38) ($top + 10) ($width - 46) 26 22 $inkFull))
    $inventoryArea.Add((Text "t_io_hand${k}_empty" "empty" ($left + 38) ($top + 10) ($width - 46) 26 22 $inkSoft))
    Export $InventoryPath "t_io_hand${k}_name"
    Export $InventoryPath "t_io_hand${k}_empty"
}
$inventoryArea.Add((Text t_io_machete "Machete" ($left + 38) 178 ($width - 46) 26 22 $inkFull))

# Items shows a window of ten rows that follows the selection.
Heading inventory t_io_items "ITEMS" $left 226
List inventory item $left 276 10 32

# The right page, below the post-it: the belt.
Heading inventory t_io_belt "BELT" $right $Below
$belt = @(
    @{ Name = "Syrettes"; Material = "hud_icon_seringe"; Blending = "Normal" },
    @{ Name = "Malaria pills"; Material = "hud_icon_pills"; Blending = "Normal" },
    @{ Name = "M67 grenades"; Material = "io_gun_m67"; Blending = "Multiply" },
    @{ Name = "Molotovs"; Material = "io_gun_molotov"; Blending = "Multiply" })
for ($j = 1; $j -le $belt.Count; $j++) {
    $top = $Below + 30 + ($j - 1) * 40
    Selection inventory "belt$j" ($right - 14) ($top + 20) ($width + 14)
    $inventoryArea.Add((Image "i_io_belt$j" $belt[$j - 1].Material "" $right ($top + 5) 30 30 "FFFFFFFF" 0 @(0, 0, 1, 1) $belt[$j - 1].Blending))
    $inventoryArea.Add((Text "t_io_belt$j" $belt[$j - 1].Name ($right + 40) ($top + 7) 260 26 22 $inkFull))
    $inventoryArea.Add((Text "t_io_belt${j}_count" "" ($right + 300) ($top + 7) 76 26 20 $inkFull RIGHT))
    Export $InventoryPath "t_io_belt${j}_count"
}

# Papers: letters and photos on the left, the Jackal and Predecessor tapes on the right below the
# post-it.
Heading papers t_io_letters "LETTERS AND PHOTOS" $left
List papers letter $left 98 10 40
Heading papers t_io_tapes "TAPES" $right $Below
List papers tape $right ($Below + 52) 4 40 io_tape 40 20 (Colour "000000" 0.8)

# Journal: the reputation in the game's words on the left; on the right the current job, its
# objectives and the Underground's, as the pause menu's main page showed them.
Heading journal t_io_reputation "" $left
$areas.journal.Add((Text t_io_reputation_prose "" $left 80 $width 400 22 $inkFull LEFT 0 TOP $true))
Heading journal t_io_jobs "JOB" $right
$areas.journal.Add((Text t_io_job "" $right 80 $width 120 22 $inkFull LEFT 0 TOP $true))
$areas.journal.Add((Text t_io_jobs_none "No job right now." $right 80 $width 22 18 $countInk))
Heading journal t_io_objectives "OBJECTIVES" $right 216
foreach ($k in 1, 2) {
    $areas.journal.Add((Text "t_io_objective$k" "" $right (252 + ($k - 1) * 68) $width 64 20 $inkFull LEFT 0 TOP $true))
}
Heading journal t_io_underground "UNDERGROUND" $right 400
$areas.journal.Add((Text t_io_underground_job "" $right 436 $width 88 20 $inkFull LEFT 0 TOP $true))
foreach ($name in "t_io_reputation", "t_io_reputation_prose", "t_io_job", "t_io_jobs_none", "t_io_objectives",
    "t_io_objective1", "t_io_objective2", "t_io_underground", "t_io_underground_job") {
    Export (AreaPath a_io_journal) $name
}

# Contacts, on the left page: the primary and secondary buddy, then the other buddies, each with a
# portrait the plugin takes from a_io_portraits and a status.
Heading contacts t_io_actives "ACTIVE BUDDIES" $left
List contacts active $left 98 2 50 io_buddy_frank 44 44 "FFFFFFFF" 130 $true
Heading contacts t_io_others "OTHER BUDDIES" $left 236
List contacts other $left 288 6 50 io_buddy_frank 44 44 "FFFFFFFF" 130 $true
$palette = foreach ($portrait in $Portraits) {
    Image "i_io_portrait_$portrait" "io_buddy_$portrait" "" 0 0 44 44
    Export (AreaPath a_io_portraits) "i_io_portrait_$portrait"
}

foreach ($tabName in $areas.Keys) {
    $page.Add((Instance "a_io_$tabName" 0 0 1 1 0))
    Export $PagePath "a_io_$tabName"
}
$page.Add((Instance a_io_portraits 0 0 1 1 0))
Export $PagePath a_io_portraits

# The post-it, in its own pixels; its instance tilts it about its centre.
$postIt.Add((Image i_io_postit_paper postit "\common.mgb" 0 0 416 302 "FFFFFFFF" 0 (Crop $PostItCrop)))
# The paperclip lies on its side; it is turned a quarter clockwise to stand across the post-it's top.
$postIt.Add((Image i_io_postit_clip paperclip "\common.mgb" (300 - 48) (6 - 24) 96 48 "FFFFFFFF" 90))
# The wide pictures, the guns' and the machete's, are kept small enough not to look blocky.
foreach ($pic in $GunPictures) {
    $postIt.Add((Image "i_io_postit_pic_$pic" "io_gun_$pic" "" 94 29 230 58 "FFFFFFFF" 0 @(0, 0, 1, 1) Multiply))
    Export $PostItPath "i_io_postit_pic_$pic"
}
$postIt.Add((Image i_io_postit_pic_machete hud_icon_machete "" 94 29 230 58))
$postIt.Add((Image i_io_postit_pic_mortar hud_icon_mortar_shop "" 94 29 230 58 (Colour "000000" 0.8)))
Export $PostItPath i_io_postit_pic_machete
Export $PostItPath i_io_postit_pic_mortar
# The belt's and the wallet's pictures are square.
foreach ($square in @(
        @{ Pic = "diamond"; Material = "ui_hud_icon_diamond"; Tint = "FFFFFFFF"; Blending = "Normal" },
        @{ Pic = "seringe"; Material = "hud_icon_seringe"; Tint = "FFFFFFFF"; Blending = "Normal" },
        @{ Pic = "pills"; Material = "hud_icon_pills"; Tint = "FFFFFFFF"; Blending = "Normal" },
        @{ Pic = "m67"; Material = "io_gun_m67"; Tint = "FFFFFFFF"; Blending = "Multiply" },
        @{ Pic = "molotov"; Material = "io_gun_molotov"; Tint = "FFFFFFFF"; Blending = "Multiply" })) {
    $postIt.Add((Image "i_io_postit_pic_$($square.Pic)" $square.Material "" 173 22 72 72 $square.Tint 0 @(0, 0, 1, 1) $square.Blending))
    Export $PostItPath "i_io_postit_pic_$($square.Pic)"
}
# The papers' pictures in `area`, each named <name>_<picture> as the plugin asks for it: a picture
# `size` square at (x, y), and a photo `photoW` wide at (photoX, photoY).
function PaperPictures($area, [string]$name, [string]$path, [double]$x, [double]$y, [double]$size,
                       [double]$photoX, [double]$photoY, [double]$photoW) {
    foreach ($paper in $PaperPictures) {
        $area.Add((Image "i_io_${name}_$paper" "io_paper_$paper" "" $x $y $size $size))
        Export $path "i_io_${name}_$paper"
    }
    foreach ($photo in $Photos) {
        $area.Add((Instance "a_io_photo_$photo" $photoX $photoY ($photoW / 1280) ($photoW / 1280) 0 "a_io_${name}_photo$photo"))
        Export $path "a_io_${name}_photo$photo"
    }
}
PaperPictures $postIt postit_pic $PostItPath 160 2 94 133 2 (94 / 800 * 1280)
$postIt.Add((Image i_io_postit_pic_tape io_tape "" 160 26 96 48 (Colour "000000" 0.8)))
$postIt.Add((Image i_io_postit_portrait io_buddy_frank "" 172 16 72 72))
Export $PostItPath i_io_postit_pic_tape
Export $PostItPath i_io_postit_portrait
$postIt.Add((Text t_io_postit_name "" 26 96 366 26 24 $inkFull))
$postIt.Add((Text t_io_postit_sub "" 26 124 366 18 15 $inkSoft))
# What a thing with no subtitle is, wrapped right under its name.
$postIt.Add((Text t_io_postit_about "" 26 128 366 110 18 $inkFull LEFT 0 TOP $true))
# Three lines of a label and a value: a gun's condition and ammo, a belt item's count, then the
# upgrades bought for it.
for ($l = 1; $l -le 3; $l++) {
    $postIt.Add((Text "t_io_postit_line$l" "" 26 (148 + ($l - 1) * 26) 120 24 20 $inkFull))
    Export $PostItPath "t_io_postit_line$l"
}
$postIt.Add((Text t_io_postit_value1 "" 101 148 100 24 20 $inkFull RIGHT))
$postIt.Add((Text t_io_postit_value2 "" 81 174 120 24 20 $inkFull RIGHT))
# A paper's or a tape's first line, a label and a longer value, and what it says below; a buddy's
# status, at the post-it's right edge.
$postIt.Add((Text t_io_postit_state "" 140 148 252 24 20 $inkFull))
$postIt.Add((Text t_io_postit_status "" 140 148 252 24 20 $inkFull RIGHT))
$postIt.Add((Text t_io_postit_quote "" 26 176 366 84 17 $inkFull LEFT 0 TOP $true))
# The game's upgrade icons, faint until bought, in a row after a line's label: a gun's operation and
# repair manuals and its bandolier on the third line, under its condition and ammo; a belt item's
# first aid manuals or bandolier on the second, under its count.
function Upgrades([string]$row, [string[]]$icons, [double]$y) {
    for ($u = 0; $u -lt $icons.Count; $u++) {
        $material = "io_upgrade_$($icons[$u])"
        $postIt.Add((Image "i_io_postit_${row}${u}_faint" $material "" (125 + $u * 27) $y 22 22 (Colour $Ink 0.35)))
        $postIt.Add((Image "i_io_postit_${row}${u}_bought" $material "" (125 + $u * 27) $y 22 22 (Colour $Ink 0.85)))
        Export $PostItPath "i_io_postit_${row}${u}_faint"
        Export $PostItPath "i_io_postit_${row}${u}_bought"
    }
}
Upgrades up "manual", "repair", "ammo" 201
Upgrades belt_up "ammo", "ammo" 175
$postIt.Add((Text t_io_postit_note "" 26 236 366 22 18 $inkFull))
foreach ($name in "t_io_postit_name", "t_io_postit_sub", "t_io_postit_about", "t_io_postit_value1", "t_io_postit_value2",
        "t_io_postit_state", "t_io_postit_status", "t_io_postit_quote", "t_io_postit_note") {
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
# The post-it's place at the right page's top, tilted about its centre, in the page's own pixels.
$page.Add((Tilted a_io_postit 512 $PostItTop 416 302 -1.4))
Export $PagePath a_io_postit

# The paper being read, over the whole right page on the post-it's paper turned upright: its name, its
# picture on the first sheet with the sheet's text below it, or the sheet's text alone, and which
# sheet of how many.
$reader = [System.Collections.Generic.List[string]]::new()
$reader.Add((Image i_io_reader_paper postit "\common.mgb" -104 104 620 412 "FFFFFFFF" 90 (Crop $PostItCrop)))
$reader.Add((Text t_io_reader_name "" 30 30 352 30 24 $inkFull))
PaperPictures $reader reader_pic $ReaderPath 76 72 260 30 92 352
$reader.Add((Text t_io_reader_text "" 30 72 352 506 20 $inkFull LEFT 0 TOP $true))
$reader.Add((Text t_io_reader_caption "" 30 346 352 232 20 $inkFull LEFT 0 TOP $true))
$reader.Add((Text t_io_reader_sheet "" 30 584 352 20 15 $inkSoft RIGHT))
foreach ($name in "t_io_reader_name", "t_io_reader_text", "t_io_reader_caption", "t_io_reader_sheet") {
    Export $ReaderPath $name
}
$page.Add((Tilted a_io_reader 509 38 412 620 1.2 $true))
Export $PagePath a_io_reader
$photoAreas = foreach ($photo in $Photos) {
    $tiles = for ($t = 0; $t -lt $PhotoTiles.Count; $t++) {
        $tile = $PhotoTiles[$t]
        Image "i_io_photo_${photo}_$($t + 1)" "io_photo_${photo}_$($t + 1)" "" $tile[0] $tile[1] $tile[2] $tile[3]
    }
    Area "a_io_photo_$photo" 1280 800 $tiles
}
# The pause menu's own page flip, common.mgb's area, which the plugin steps through itself. The pause
# pages turn forward with it at (-27, -24), 1.0390625 as tall, over notebook.png drawn 1097 by 982
# from (-58, -1); that carried into the spread's pixels, which draw notebook.png unscaled from
# (4, 10). Shown only while R turns a page.
$page.Add((Instance "#5B36589B" ((-27 + 58) / (1097 / 1024) - $Spread.X) ((-24 + 1) / (982 / 1024) - $Spread.Y) `
    (1 / (1097 / 1024)) (1.0390625 / (982 / 1024)) 0 a_io_flip $CommonId TickTimingStrategy $true))
Export $PagePath a_io_flip
$Exports.Add("<GENERICOBJECT name=`"IO_FLIP_AREA`"><LINK slot=`"44`" LASTOBJECTTYPE=`"Area`" IDS=`"$PagePath a_io_flip #5B36589B`" /></GENERICOBJECT>")
# The page's last image.
$page.Add((Image i_io_end io_mark_end "" 0 0 2 2))

function Material([string]$name, [string]$texture) {
    "<Material name=`"$name`" texture=`"$texture`" REGION=`"0 0 1 1`" />"
}
$materials = @(
    ($GunPictures + "m67", "molotov") | ForEach-Object { Material "io_gun_$_" "\textures\guns\gun_icon_$_.png" }
    ("manual", "repair", "ammo") | ForEach-Object { Material "io_upgrade_$_" "\textures\ui_icon_$($_)upgrade.png" }
    $PaperPictures | ForEach-Object { Material "io_paper_$_" "\textures\common\$_.png" }
    foreach ($photo in $Photos) {
        for ($t = 1; $t -le $PhotoTiles.Count; $t++) { Material "io_photo_${photo}_$t" "\textures\loading\${photo}_0$t.png" }
    }
    $Portraits | ForEach-Object { Material "io_buddy_$_" "\textures\hud\icons_buddies\buddies_$_.png" }
    Material io_tape "\textures\ui_tape_icon.png"
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
& $Cli xbt extract $StockTexture -o $Work | Out-Null
Add-Type -AssemblyName System.Drawing
# Their sizes are how the plugin knows them.
foreach ($mark in @(@{ Name = "mark_begin"; W = 4; H = 64 }, @{ Name = "mark_end"; W = 64; H = 4 })) {
    $clear = New-Object System.Drawing.Bitmap $mark.W, $mark.H, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $clear.Save("$Work\$($mark.Name).png", [System.Drawing.Imaging.ImageFormat]::Png)
    $clear.Dispose()
    & $Texconv -nologo -f DXT5 -m 1 -y -o $Work "$Work\$($mark.Name).png" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "texconv failed for $($mark.Name)" }
    & $Cli xbt build "$Work\$($mark.Name).dds" "$Work\ui_tape_icon.xml" -o "$Textures\$($mark.Name).xbt" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "xbt build failed for $($mark.Name)" }
}

# A changed package's XML checked and written into the layer as fragments of its retail package.
function Ship([System.Collections.Generic.List[string]]$lines, [string]$xml, [System.IO.FileInfo]$source,
              [string]$variant) {
    [IO.File]::WriteAllLines($xml, $lines)
    & $Cli mgb verify $xml
    if ($LASTEXITCODE -ne 0) { throw "verify failed for $variant $($source.Name)" }
    $target = Join-Path $Layer "$variant\ui\$($source.Name)"
    Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
    & $Cli mgb fragments $xml --base $source.FullName --out $target | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "fragments failed for $variant $($source.Name)" }
}

foreach ($source in Get-ChildItem -Recurse -Path $Retail -Filter hud.mgb) {
    $variant = $source.Directory.Parent.FullName.Substring($Retail.Length + 1)
    $dir = Join-Path $Work $variant
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item $source.FullName "$dir\hud.mgb"
    & $Cli mgb decode "$dir\hud.mgb" -o "$dir\hud.xml" | Out-Null
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
        (Area a_io_postit 416 302 $postIt)
        (Area a_io_reader 412 620 $reader)
        $photoAreas) -join "`n"
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

# The pause menu's main page without the job the Journal shows: its three headings, the job's line,
# its objective list and the Underground's line and icon.
$PausePage = "#C1849B53"
$PausePanel = "#E039A398", "#36D6DAD3", "#95871E12", "#81FDCEB5", "#B638527D", "#6CF418D5", "#C801AB46"
foreach ($source in Get-ChildItem -Recurse -Path $Retail -Filter sp_pause_menus.mgb) {
    $variant = $source.Directory.Parent.FullName.Substring($Retail.Length + 1)
    $dir = Join-Path $Work $variant
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & $Cli mgb decode $source.FullName -o "$dir\sp_pause_menus.xml" | Out-Null
    $lines = [System.Collections.Generic.List[string]]::new([IO.File]::ReadAllLines("$dir\sp_pause_menus.xml"))
    $start = IndexOf $lines "<USERDATA name=`"$PausePage`">"
    $end = IndexOf $lines '</Area>' $start
    foreach ($element in $PausePanel) {
        $at = IndexOf $lines "<USERDATA name=`"$element`">" $start
        if ($at -gt $end) { throw "$element is not on the pause page in $variant" }
        $lines[$at - 1] = $lines[$at - 1].Replace('HIDDEN="false"', 'HIDDEN="true"')
    }
    Ship $lines "$dir\sp_pause_menus.xml" $source $variant
}
