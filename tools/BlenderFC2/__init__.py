# Blender extension entry point; the add-on itself is the `addon` package.
#
# An extension zip has to expose register/unregister at its root, beside
# blender_manifest.toml, so this is what Blender loads when the folder is
# installed as an extension.

from .addon import register, unregister

__all__ = ["register", "unregister"]
