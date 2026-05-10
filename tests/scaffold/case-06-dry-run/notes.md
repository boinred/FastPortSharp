# case-06-dry-run

`--dry-run` must print a plan and exit 0 without creating any
filesystem artifact. Runner passes a non-existing `{DEST}` and
verifies it remains absent.

Design Ref: §8.3 case-06.
