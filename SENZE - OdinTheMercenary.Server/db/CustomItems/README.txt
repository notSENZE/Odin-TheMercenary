ODIN'S CUSTOM ITEMS

Put your custom item .json or .jsonc files in this folder.
Subfolders are supported.

The file format is the same one used by WTT CommonLib and Senze's Armory.
Custom weapons are automatically added to Odin when itemTplToClone points to
a weapon that is already present in Odin's FirstPrimaryWeapon pool. The cloned
weapon replaces that base weapon and inherits Odin's existing weapon mod pool.

Odin's items must remain exclusive. Do not enable any of these options:

  addtoBots
  addtoTraders
  addtoStaticAmmo
  addtoStaticLootContainers
  addtostaticlootcontainer

The server will stop with a clear error if one of them is set to true.

The file _weapon.example.json.example is ignored by the loader. Copy it to a
new file ending in .json when you want to use it.
