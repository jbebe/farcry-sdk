"""Archetype fragments for a mod's layer, cut out of an entity library decoded with
`jackall-cli fcb decode`: each archetype's node, FCSE's entity data component added to one, and the
fragment written where a layer stages it.
"""
import os
import re


def prototypes(lines):
    """Each EntityPrototype's hidName and the line range of its node."""
    for start, line in enumerate(lines):
        if line.strip() != '<object hash="256A1FF9">':
            continue
        indent = line[:len(line) - len(line.lstrip())]
        end = next(i for i in range(start + 1, len(lines))
                   if lines[i].startswith(indent + "</object>"))
        name = next(re.search(r">([^<]*)<", l).group(1) for l in lines[start:end]
                    if '<value name="hidName"' in l)
        yield name, start, end + 1


def object_lines(block, kind):
    """The lines that open and close the one object of this type in the block, and its indent."""
    first = next(i for i, l in enumerate(block) if l.strip() == '<object type="%s">' % kind)
    indent = block[first][:len(block[first]) - len(block[first].lstrip())]
    last = next(i for i in range(first + 1, len(block)) if block[i].startswith(indent + "</object>"))
    return first, last, indent


def add_entity_data(block, values):
    """Adds FCSE's entity data component to the archetype's components, a key for each value."""
    _, last, indent = object_lines(block, "Components")
    inner = indent + "  "
    added = [inner + '<object type="CFCSEDataComponent">']
    for key, value in values.items():
        name, kind = (("Float", "Float") if isinstance(value, float)
                      else ("Int", "Int32") if isinstance(value, int)
                      else ("String", "String"))
        added += [inner + '  <object type="%s">' % key,
                  inner + '    <value name="%s" type="%s">%s</value>' % (name, kind, value),
                  inner + "  </object>"]
    block[last:last] = added + [inner + "</object>"]


def fragment_path(layer, library, archetype):
    """Where a layer stages the archetype's fragment of this library."""
    return os.path.join(layer, *library, *archetype.split(".")) + ".xml"


def read(path):
    """A fragment file's lines and the line ending it uses."""
    with open(path, encoding="utf-8", newline="") as handle:
        text = handle.read()
    return text.splitlines(), "\r\n" if "\r\n" in text else "\n"


def write(path, block, newline="\n"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(newline.join(block) + newline)
