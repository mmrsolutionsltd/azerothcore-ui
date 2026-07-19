# Spell metadata

`spell-metadata.json` is application-owned, read-only display metadata for the
World of Warcraft 3.3.5a client build 12340. It contains only spell ID, English
name, rank/subtext, and the learned-spell target for spells whose client effect
is `LEARN_SPELL`. The latter resolves recipe items to the recipe spell they teach.

The data was generated from `spell_dbc.sql` in the `AzerothcoreDBCToSQL.zip`
asset published by the `wowgaming/client-data` `dbc_sql_v1` release:

https://github.com/wowgaming/client-data/releases/tag/dbc_sql_v1

This catalogue is embedded in the API assembly. It is not imported into any
AzerothCore schema and is not a replacement for AzerothCore's server-side
`spell_dbc` override table.

To regenerate it after downloading and extracting the release asset:

```text
python generate_spell_metadata.py <path-to-spell_dbc.sql> spell-metadata.json
```
