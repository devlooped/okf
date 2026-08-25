---
name: okf
description: >
  Work with Open Knowledge Format (OKF) markdown knowledge bundles using
  `dnx okf`. Use when validating a bundle, emitting okf.json, generating the
  HTML reader, or dumping the bundled OKF spec. Triggers:
  OKF, okf check, okf graph, okf view, okf spec, knowledge bundle.
license: MIT
---

## What's OKF

Authoritative text: `dnx okf -- spec`. This digest is for in-context authoring.

- Bundle: dir of UTF-8 `.md`. Reserved: `index.md`, `log.md`. All other `.md` = concepts. id = path in bundle without `.md`.
- Concept: YAML frontmatter + body. REQUIRED: `type` (unregistered string). RECOMMENDED: `title`, `description`, `resource`, `tags`. Extra keys allowed.
- `index.md`: no frontmatter except root MAY have `okf_version`. Body = heading + `* [Title](rel) - desc` lists. Subdirs as `* [Name](dir/) - desc`.
- `log.md`: `# …` then `## YYYY-MM-DD` newest-first; entries are prose (`* **Update**: …` is convention).
- Actors: `<agent>/<model+version>` | `human:<id>` | `process:<id>`.
- `generated: { by, at }` — `by` required if present; `at` = last meaningful content change (ISO 8601).
- `verified`: `{ by, at }` or list of same. Bare mapping ≡ one-element list. Tiers: absent → unverified; non-`human:` only → machine-confirmed; any `human:` → human-reviewed.
- `status`: `draft` | `stable` | `deprecated`. Absent ⇒ `stable`.
- `stale_after`: `YYYY-MM-DD`. Stale when `today >= stale_after`.
- `sources[]`: each `resource` required (URL | `/bundle-path` | relative | scope descriptor). Optional: `id`, `title`, `author`, `usage_count`, `last_modified`. Sibling `usage_window: { from, to }` (per-entry override allowed). Per-claim: `[^id]` footnote keyed to `sources[].id`.
- Links: `/bundle-relative.md` preferred; relative ok. Broken links tolerated.
- Path-valued: `resource`, `sources[].resource` → URL | `/abs` | relative.
- Conventional headings when applicable: `# Schema`, `# Examples`.
- Conformance: every non-reserved `.md` has parseable YAML + non-empty `type`; reserved files follow their shapes. Missing optional families ok. Unknown `type`/keys ok. Do not reject for broken links or missing indexes.

Need a rule not listed here: `dnx okf -- spec`.

# okf tool

> Requires .NET 10 SDK

Always invoke via `dnx okf`. Format rules: `dnx okf -- spec` (optional `-v 0.2`).
Do not fetch the spec over the network. `schema` is the graph JSON Schema for
`okf.json`, not the format spec.

## Commands

| Command | Purpose |
|---------|---------|
| `check [path]` | Validate structure, frontmatter, indexes, log, links |
| `graph [path]` | Emit `okf.json` (`-b`/`--body`, `--nav`, `--js`, `-o`) |
| `schema [-v ver] [-o file]` | Graph JSON Schema (`okf.json` shape) to stdout or file |
| `spec [-v ver] [-o file]` | OKF spec markdown to stdout or file |
| `view [path]` | HTML reader + full body+nav graph |
| `skill [dir] [-g]` | Install this skill (`skill remove` to uninstall) |

`-v` / `--version` selects a bundled format version (`latest` by default).
Pass an explicit version when a bundle declares `okf_version`. Unknown
versions error with the list of bundled ones.

```bash
dnx okf -- spec
dnx okf -- spec -v 0.2 -o SPEC.md
```

Graph files carry `"$schema": "https://www.schemastore.org/okf-0.2.json"`
and `generated: { by: "okf/0.2", at: … }`. That `okf/…` actor is the graph
producer only — never use it on a concept.

## Write

```yaml
generated: { by: <agent>/<model+version>, at: <ISO 8601> }
```

Example: `xai/grok-4.6`. Humans: `human:<id>`. Jobs: `process:<id>`.
Do not invent `verified` events. Prefer `sources` over a body `# Citations` list.

## Workflow

1. `dnx okf -- check ./bundle`
2. Agents/APIs: `dnx okf -- graph ./bundle -o ./bundle/okf.json`
3. Humans: `dnx okf -- view ./bundle --open`
