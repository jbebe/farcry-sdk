#!/bin/bash
# extract.sh <decoded.xml> <hidName> <outfile>
# Prints the EntityPrototype node (hash 256A1FF9) whose Entity has this hidName.
set -euo pipefail
XML="$1"; HID="$2"; OUT="$3"
N=$(grep -n "<value name=\"hidName\" type=\"String\">${HID}</value>" "$XML" | cut -d: -f1)
[ "$(printf '%s\n' "$N" | wc -l)" -eq 1 ] || { echo "hidName $HID not unique: $N" >&2; exit 1; }
START=$((N-3))
sed -n "${START}p" "$XML" | grep -q '<object hash="256A1FF9">' || { echo "no 256A1FF9 at $START" >&2; exit 1; }
awk -v s="$START" 'NR>=s { print; if (NR>s && $0=="    </object>") exit }' "$XML" > "$OUT"
echo "$HID -> $OUT ($(wc -l < "$OUT") lines)"
