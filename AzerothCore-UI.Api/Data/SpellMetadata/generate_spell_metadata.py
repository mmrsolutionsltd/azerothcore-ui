"""Generate the compact app-owned spell catalogue from a 3.3.5a spell_dbc SQL export."""

import csv
import json
import re
import sys
from pathlib import Path


def main(source_name: str, destination_name: str) -> None:
    source = Path(source_name)
    destination = Path(destination_name)
    rows: list[dict[str, object]] = []
    column_indexes: dict[str, int] | None = None

    with source.open("r", encoding="utf-8-sig", errors="strict") as sql_file:
        statement = ""
        for line in sql_file:
            if not statement and not line.startswith("INSERT INTO"):
                continue

            statement += line
            if not statement.rstrip().endswith(");"):
                continue

            match = re.match(
                r"INSERT INTO .*? \((.*?)\) VALUES \((.*)\);\s*$",
                statement,
                re.DOTALL,
            )
            if match is None:
                raise ValueError("Unexpected spell_dbc INSERT format")

            if column_indexes is None:
                columns = re.findall(r"`([^`]+)`", match.group(1))
                column_indexes = {name: index for index, name in enumerate(columns)}

            values = next(csv.reader(
                [match.group(2)],
                delimiter=",",
                quotechar='"',
                escapechar="\\",
                doublequote=False,
            ))

            spell_id = int(values[column_indexes["ID"]])
            name = values[column_indexes["Name_Lang_enUS"]]
            rank = values[column_indexes["NameSubtext_Lang_enUS"]] or None
            learned_spell_id = next((
                int(values[column_indexes[f"EffectTriggerSpell_{effect}"]])
                for effect in range(1, 4)
                if int(values[column_indexes[f"Effect_{effect}"]]) == 36
                and int(values[column_indexes[f"EffectTriggerSpell_{effect}"]]) > 0
            ), None)
            if name:
                row: dict[str, object] = {"Id": spell_id, "Name": name}
                if rank:
                    row["Rank"] = rank
                if learned_spell_id:
                    row["LearnedSpellId"] = learned_spell_id
                rows.append(row)

            statement = ""

    destination.write_text(
        json.dumps(rows, ensure_ascii=False, separators=(",", ":")),
        encoding="utf-8",
    )
    print(f"Generated {len(rows):,} spell metadata records at {destination}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: generate_spell_metadata.py <spell_dbc.sql> <output.json>")
    main(sys.argv[1], sys.argv[2])
