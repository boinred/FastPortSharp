# case-03-regex-meta

Negative case: `My$Game` violates the name regex
`^[A-Z][A-Za-z0-9]{0,63}$` (special char) → exit 2,
stderr contains "does not match".

Design Ref: §8.3 case-03.
