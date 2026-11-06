# Map Overhaul

A realistic map in place of the game's drawn one, and the GPS removed. Nothing built yet; the idea is
issue #29.

## The full map

`stitch_map.py` joins world 1's 25 area maps, `levels\w1_<row a-e>_<column 1-5>\mapcompass_mip0.xbt`,
one per level cell, into `assets/world1-map.png`: 5120 px a side at 1 m per pixel, row `a` (the highest
Y) at the top and column `1` (the lowest X) at the left. The tiles go in as stored and meet without a
seam. The picture is the game's own, so it stays local; make it with

```
python mods\map-overhaul\stitch_map.py --game "C:\Games\Far Cry 2"
```

It needs Pillow and a Release build of `jackall-cli`.
