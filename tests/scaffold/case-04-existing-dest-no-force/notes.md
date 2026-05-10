# case-04-existing-dest-no-force

Negative case: dest already contains a file → without `--force`,
script must refuse with exit 3.

Runner copies `input/pre/*` into the working dest BEFORE invoking
the scaffold script.

Design Ref: §8.3 case-04.
