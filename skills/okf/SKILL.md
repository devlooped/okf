---
name: okf
description: >
  Work with Open Knowledge Format (OKF) markdown knowledge bundles using
  `dnx okf`. Use when validating a bundle, emitting okf.json, generating the
  HTML reader, or dumping the bundled graph schema / OKF spec. Triggers:
  OKF, okf check, okf graph, okf view, okf schema, okf spec, knowledge bundle.
license: MIT
---

# okf

Always invoke via `dnx okf`. Dump the spec and graph schema from the tool —
do not fetch them over the network.

## Commands

| Command | Purpose |
|---------|---------|
| `check [path]` | Validate structure, frontmatter, indexes, log, links |
| `graph [path]` | Emit `okf.json` (`-b`/`--body`, `--nav`, `--js`, `-o`) |
| `schema [-v ver] [-o file]` | Graph JSON Schema to stdout or file |
| `spec [-v ver] [-o file]` | OKF spec markdown to stdout or file |
| `view [path]` | HTML reader + full body+nav graph |
| `skill [dir]` | Install this skill (`skill remove` to uninstall) |

`-v` / `--version` selects a bundled format version (`latest` by default).
Pass an explicit version when a bundle declares `okf_version` (or when you
need to compare formats). Unknown versions error with the list of bundled
ones.

```bash
dnx okf -- schema
dnx okf -- schema -v 0.1 -o okf-0.1.json
dnx okf -- spec
dnx okf -- spec -v latest -o SPEC.md
```

Generated graphs include `"$schema": "https://www.schemastore.org/okf-0.1.json"`.

## Workflow

1. `dnx okf -- check ./bundle`
2. Agents/APIs: `dnx okf -- graph ./bundle -o ./bundle/okf.json`
3. Humans: `dnx okf -- view ./bundle --open`

Concept **id** is the path within the bundle without `.md`. Prefer `spec` /
`schema` over the public GitHub copy when you need the rules in-session.
