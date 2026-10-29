"""Archetype fragments for a mod's layer, cut out of an entity library decoded with
`jackall-cli fcb decode`: each archetype's node, its values changed, FCSE's entity data component
added to one, and the fragment written where a layer stages it.
"""
import os
import re
import zlib


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


def crc(text):
    """The Hash a String value's text_ twin is stored as."""
    return "%08X" % zlib.crc32(text.encode())


def find_value(text, name, kind):
    """The archetype's first value `name` of type `kind`, as text."""
    return re.search(r'<value name="%s" type="%s">([^<]*)</value>' % (name, kind), text).group(1)


def replace_value(text, name, kind, old, new):
    """The archetype's text with its one value `name` changed from `old` to `new`."""
    element = '<value name="%s" type="%s">%s</value>'
    assert text.count(element % (name, kind, old)) == 1, name
    return text.replace(element % (name, kind, old), element % (name, kind, new))


def replace_hashed(text, name, old, new):
    """The archetype's text with the String `text_<name>` and its Hash `name` changed to `new`."""
    text = replace_value(text, "text_" + name, "String", old, new)
    return replace_value(text, name, "Hash", crc(old), crc(new))


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


def write(path, block):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write("\n".join(block) + "\n")
