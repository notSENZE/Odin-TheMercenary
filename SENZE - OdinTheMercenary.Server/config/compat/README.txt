WEAPON SLOT COMPAT RULES

Put .json or .jsonc files into this folder:

  config/compat/weapons

Subfolders are supported.

TARGET-TPL
  The weapon or attachment whose slots should be changed.

ALLOWED_TPLS
  Item or category IDs that should be allowed in the target.

SLOT_NAMES
  One or more exact slot names such as mod_magazine or mod_scope.

REPLACE
  Keep this false to add compatibility without removing existing entries.

The example files end in .example and are ignored by the loader. Copy and
rename one to .jsonc when you want to use it.
