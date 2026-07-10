# okf

[![Version](https://img.shields.io/nuget/vpre/okf.svg?color=royalblue)](https://www.nuget.org/packages/okf)
[![Downloads](https://img.shields.io/nuget/dt/okf.svg?color=darkmagenta)](https://www.nuget.org/packages/okf)
[![EULA](https://img.shields.io/badge/EULA-OSMF-blue?labelColor=black&color=C9FF30)](https://github.com/devlooped/oss/blob/main/osmfeula.txt)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/devlooped/oss/blob/main/license.txt)

<!-- include https://github.com/devlooped/.github/raw/main/osmf.md -->
## Open Source Maintenance Fee

To ensure the long-term sustainability of this project, users of this package who generate 
revenue must pay an [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). 
While the source code is freely available under the terms of the [License](license.txt), 
this package and other aspects of the project require [adherence to the Maintenance Fee](osmfeula.txt).

To pay the Maintenance Fee, [become a Sponsor](https://github.com/sponsors/devlooped) at the proper 
OSMF tier. A single fee covers all of [Devlooped packages](https://www.nuget.org/profiles/Devlooped).

<!-- https://github.com/devlooped/.github/raw/main/osmf.md -->
<!-- #content -->
## Overview

**okf** is a .NET tool for working with [Open Knowledge Format](src/okf/SPEC.md) (OKF) bundles —
directories of markdown concepts with YAML frontmatter, linked by relative paths.

It validates bundles, builds a knowledge **graph** (nodes + edges + optional body/nav), and
emits interactive HTML for reading (`view`) and exploring relationships (`viz`).

Run without installing permanently via [`dnx`](https://learn.microsoft.com/dotnet/core/tools/dotnet-dnx):

```bash
dnx okf -- --help
```

While the package is pre-release, pass `--prerelease` to `dnx` (before the tool args):

```bash
dnx okf --prerelease -- check samples/the-law
```

| Command | Purpose |
|---------|---------|
| [`check`](#check) | Validate a bundle (structure, frontmatter, links) |
| [`graph`](#graph) | Emit `okf.json` / `okf.js` knowledge graph |
| [`view`](#view) | Obsidian-style single-file reader (`index.html` + full graph) |
| [`viz`](#viz) | Interactive Cytoscape graph HTML |

The sample bundle under [`samples/the-law`](samples/the-law) is used in the screenshots below
(Frédéric Bastiat’s *The Law* as a knowledge corpus).

## Install / run

```bash
# One-shot (downloads tool package as needed)
dnx okf -- check samples/the-law

# Or install as a local/global tool
dotnet tool install okf --prerelease
okf check samples/the-law
```

Use `--` after `dnx okf` when you need to pass flags that might otherwise be parsed by `dnx`
itself (for example `dnx okf -- --help`).

---

## `check`

Validate an OKF bundle directory for structural and content issues.

```bash
dnx okf -- check [path] [--json]
```

| Argument / option | Description |
|-------------------|-------------|
| `path` | Bundle directory (default: `.`) |
| `--json` | Emit issues as JSON instead of human-readable text |

**What it checks (errors):**

- Bundle directory exists
- Concept files have valid YAML frontmatter
- Concept files declare a non-empty `type`
- `index.md` frontmatter is valid (root index may only use `okf_version`)
- `index.md` structure and list entries are valid
- `log.md` format is valid

**Warnings:**

- Unresolved internal links
- Free prose lines in `index.md` (non-structural)

Exit code is `1` when any errors are reported, `0` otherwise.

```bash
dnx okf -- check samples/the-law
# ✓ Bundle directory exists
# ✓ Concept files have valid YAML frontmatter
# …

dnx okf -- check samples/check-failures
# intentionally invalid showcase (see samples/README.md)
```

---

## `graph`

Generate an OKF graph file for the bundle. Runs validation first; generation is skipped if
there are errors.

```bash
dnx okf -- graph [path] [-o|--out <path>] [-b|--body] [--nav] [--js] [-q|--quiet] [--json] [-p|--properties Key=Value]
```

| Argument / option | Description |
|-------------------|-------------|
| `path` | Bundle directory (default: `.`) |
| `-o`, `--out` | Output path (default: `okf.json`, or `okf.js` with `--js`) |
| `-b`, `--body` | Include markdown body text on each node |
| `--nav` | Include the index-driven navigation tree |
| `--js` | Emit a script that sets `window.data` (loadable via `<script src>` on `file://`) |
| `-q`, `--quiet` | Only render validation errors and warnings |
| `--json` | Print validation issues as JSON |
| `-p`, `--properties` | Extra bundle-level properties (`Key=Value`, repeatable) |

Examples:

```bash
# Compact graph (metadata + link edges + PageRank metrics)
dnx okf -- graph samples/the-law -o samples/the-law/okf.json

# Full graph for offline consumers / custom UIs
dnx okf -- graph samples/the-law --body --nav -o samples/the-law/okf-full.json

# JS global for file:// hosting
dnx okf -- graph samples/the-law --js -o samples/the-law/okf.js
```

### Graph schema

Top-level shape:

```json
{
  "version": "0.1",
  "timestamp": "2026-07-10T05:21:13.1336661+00:00",
  "nav": { },
  "nodes": [ ],
  "edges": [ ],
  "bundle": { }
}
```

| Field | When present | Description |
|-------|--------------|-------------|
| `version` | always | Graph format version (`0.1`) |
| `timestamp` | always | Generation time (UTC offset) |
| `nodes` | always | Concept nodes (one per concept `.md`) |
| `edges` | always | Directed links from markdown references |
| `nav` | `--nav` | Index-driven tree for progressive disclosure |
| `bundle` | `-p` used | Producer-defined key/value properties |

#### Nodes

Each concept becomes a node. Concept **id** is the path within the bundle without `.md`
(for example `fundamental-principles/natural-rights`).

| Field | Source | Description |
|-------|--------|-------------|
| `id` | path | Stable concept id |
| `slug` | derived | Short unique abbreviation (edge ids / fragments) |
| `type` | frontmatter | Required concept type |
| `title` | frontmatter | Display title |
| `label` | frontmatter / title | Short label (nav, graph UI) |
| `description` | frontmatter | One-line summary |
| `resource` | frontmatter | Optional canonical URI for an underlying asset |
| `tags` | frontmatter | Optional tag list |
| `timestamp` | frontmatter | Optional last-modified |
| `path` | file | Relative path to the `.md` file |
| `body` | file (`--body`) | Markdown body after frontmatter |
| `degree` / `in` / `out` | graph | Link counts |
| `weight` / `rank` | PageRank | Importance score and dense rank (1 = highest) |

Plus any other frontmatter keys are preserved as extension data on the node.

**Sample nodes** (from `samples/the-law`, compact graph — no `body`):

```json
{
  "version": "0.1",
  "timestamp": "2026-07-10T05:21:13.1336661+00:00",
  "nodes": [
    {
      "id": "foreword",
      "slug": "f",
      "type": "Foreword",
      "title": "Foreword by Thomas J. DiLorenzo",
      "label": "Foreword",
      "description": "Places the essay in the tradition of the Declaration of Independence and warns against modern forms of legal plunder.",
      "tags": ["bastiat", "foreword", "legal-philosophy", "the-law"],
      "timestamp": "2026-07-02T12:00:00+00:00",
      "path": "foreword.md",
      "degree": 5,
      "in": 0,
      "out": 5,
      "weight": 0.0018987341772151902,
      "rank": 10
    },
    {
      "id": "fundamental-principles/natural-rights",
      "slug": "fna",
      "type": "Core Principle",
      "title": "Natural Rights",
      "label": "Natural Rights",
      "description": "Life, liberty and property are God-given and exist prior to any legislation.",
      "tags": ["bastiat", "legal-philosophy", "natural-rights", "the-law"],
      "timestamp": "2026-07-02T12:00:00+00:00",
      "path": "fundamental-principles/natural-rights.md",
      "degree": 70,
      "in": 62,
      "out": 8,
      "weight": 0.11198154345997116,
      "rank": 5
    }
  ],
  "edges": [
    {
      "source": "foreword",
      "target": "the-law",
      "id": "f_t",
      "label": "The Law"
    },
    {
      "source": "foreword",
      "target": "fundamental-principles/natural-rights",
      "id": "f_fna",
      "label": "Natural Rights"
    },
    {
      "source": "foreword",
      "target": "fundamental-principles/law-as-organization-of-defense",
      "id": "f_flaw-",
      "label": "the definition of law"
    }
  ]
}
```

With `--body`, nodes also include the markdown body:

```json
{
  "id": "foreword",
  "slug": "f",
  "type": "Foreword",
  "title": "Foreword by Thomas J. DiLorenzo",
  "body": "# Foreword\n\nThese principles are developed in [The Law](/the-law.md) and rest on [Natural Rights](/fundamental-principles/natural-rights.md).\n…",
  "path": "foreword.md",
  "degree": 5,
  "in": 0,
  "out": 5,
  "weight": 0.0018987341772151902,
  "rank": 10
}
```

#### Edges

Edges are directed links discovered in concept markdown (relative and bundle-rooted `/…` paths).
External URLs are ignored.

| Field | Description |
|-------|-------------|
| `source` | Concept id of the linking document |
| `target` | Concept id of the linked document |
| `id` | Short edge id from source/target slugs |
| `label` | Link text when available |

#### Nav (`--nav`)

Index-driven tree used by `view`. Node kinds:

| `kind` | Meaning |
|--------|---------|
| `dir` | Directory (has optional `body` from `index.md`) |
| `group` | Section heading group inside an index |
| `concept` | Leaf pointing at a concept `id` |
| `orphans` | Concepts not listed from any index |

```json
{
  "kind": "dir",
  "id": "",
  "label": "The Law — Knowledge Bundle",
  "body": "\n# The Law — Knowledge Bundle\n\n## Primary Documents\n…",
  "children": [
    {
      "kind": "group",
      "label": "Primary Documents",
      "children": [
        {
          "kind": "concept",
          "id": "the-law",
          "label": "The Law",
          "description": "Complete overview and analysis"
        },
        {
          "kind": "concept",
          "id": "foreword",
          "label": "Foreword",
          "description": "Thomas DiLorenzo"
        }
      ]
    }
  ]
}
```

---

## `view`

Generate an Obsidian-style single-file reader (`index.html`) plus a full body+nav `okf.json`.
Always builds with concept bodies and the index-driven nav tree. Default writes both files into
the bundle root (overwrites an existing compact `okf.json`).

```bash
dnx okf -- view [path] [-o|--out <path>] [--name <title>] [--open]
```

| Argument / option | Description |
|-------------------|-------------|
| `path` | Bundle directory (default: `.`) |
| `-o`, `--out` | Output directory, or path to `index.html` / `okf.json` (extension-less paths are directories) |
| `--name` | Display name in the HTML title (default: directory name) |
| `--open` | Open the generated `index.html` in the default browser |

```bash
dnx okf -- view samples/the-law --name "The Law" --open
```

### Reader features

The generated viewer is a self-contained HTML app (marked + DOMPurify + 3d-force-graph):

- **Navigation tree** from `index.md` groups and directories, with search
- **Concept reader** — type chip, title, description, tags, rendered markdown
- **Backlinks** — “Linked from” list derived from graph edges
- **Local graph** — force-directed neighborhood of the current concept (expandable)
- **Tags panel** — co-occurrence graph across the corpus
- **Light / dark theme** (persisted in `localStorage`)

Screenshots from [`samples/the-law/index.html`](samples/the-law/index.html):

Bundle home (dark theme) — nav, index body, local graph, on-this-page:

![](https://raw.githubusercontent.com/devlooped/okf/main/assets/img/view-overview.png)

Concept page with tags, backlinks, and neighborhood graph:

![](https://raw.githubusercontent.com/devlooped/okf/main/assets/img/view-concept.png)

Expanded local graph for a highly connected concept (*Natural Rights*):

![](https://raw.githubusercontent.com/devlooped/okf/main/assets/img/view-local-graph.png)

Tags panel — tag co-occurrence force graph:

![](https://raw.githubusercontent.com/devlooped/okf/main/assets/img/view-tags.png)

Light theme:

![](https://raw.githubusercontent.com/devlooped/okf/main/assets/img/view-overview-light.png)

---

## `viz`

Generate an interactive HTML visualization (Cytoscape) from a **bundle directory** or an
existing **graph JSON** file.

```bash
dnx okf -- viz [path] [-o|--out <path>] [--name <title>] [--open]
```

| Argument / option | Description |
|-------------------|-------------|
| `path` | Bundle directory or `.json` graph file (default: `.`) |
| `-o`, `--out` | Output HTML path (default: `viz.html` next to the input) |
| `--name` | Display name in the visualization title |
| `--open` | Open the generated HTML after writing |

When given a directory, `viz` validates the bundle, builds a graph with bodies, then writes HTML.
When given a `.json` graph, it visualizes that file as-is (handy after `graph --body`).

```bash
dnx okf -- viz samples/the-law -o samples/the-law/viz.html --open
dnx okf -- viz samples/the-law/okf.json --name "The Law"
```

The viz UI supports search, type filter, layout presets (cose, concentric, breadth-first, circle, grid),
and a detail pane for the selected node (frontmatter + rendered body when present).

---

## Typical workflow

```bash
# 1. Author markdown concepts under a bundle directory
# 2. Validate
dnx okf -- check ./my-bundle

# 3a. Ship a compact graph for agents / APIs
dnx okf -- graph ./my-bundle -o ./my-bundle/okf.json

# 3b. Or ship a human reader
dnx okf -- view ./my-bundle --open

# 3c. Or explore link topology
dnx okf -- viz ./my-bundle --open
```

## OKF format (brief)

A **bundle** is a directory of UTF-8 markdown files:

- Concept files: YAML frontmatter (`type` required) + free-form body
- `index.md` — optional directory listing (progressive disclosure)
- `log.md` — optional chronological update log

Concept **id** = relative path without `.md`. Links between concepts use normal markdown links;
`graph` / `view` / `viz` turn those into edges.

See the full draft specification embedded in the tool package and in
[`src/okf/SPEC.md`](src/okf/SPEC.md).
<!-- #content -->
---
<!-- include https://github.com/devlooped/sponsors/raw/main/footer.md -->
# Sponsors 

<!-- sponsors.md -->
[![Clarius Org](https://avatars.githubusercontent.com/u/71888636?v=4&s=39 "Clarius Org")](https://github.com/clarius)
[![MFB Technologies, Inc.](https://avatars.githubusercontent.com/u/87181630?v=4&s=39 "MFB Technologies, Inc.")](https://github.com/MFB-Technologies-Inc)
[![SandRock](https://avatars.githubusercontent.com/u/321868?u=99e50a714276c43ae820632f1da88cb71632ec97&v=4&s=39 "SandRock")](https://github.com/sandrock)
[![DRIVE.NET, Inc.](https://avatars.githubusercontent.com/u/15047123?v=4&s=39 "DRIVE.NET, Inc.")](https://github.com/drivenet)
[![Keith Pickford](https://avatars.githubusercontent.com/u/16598898?u=64416b80caf7092a885f60bb31612270bffc9598&v=4&s=39 "Keith Pickford")](https://github.com/Keflon)
[![Thomas Bolon](https://avatars.githubusercontent.com/u/127185?u=7f50babfc888675e37feb80851a4e9708f573386&v=4&s=39 "Thomas Bolon")](https://github.com/tbolon)
[![Kori Francis](https://avatars.githubusercontent.com/u/67574?u=3991fb983e1c399edf39aebc00a9f9cd425703bd&v=4&s=39 "Kori Francis")](https://github.com/kfrancis)
[![Reuben Swartz](https://avatars.githubusercontent.com/u/724704?u=2076fe336f9f6ad678009f1595cbea434b0c5a41&v=4&s=39 "Reuben Swartz")](https://github.com/rbnswartz)
[![Jacob Foshee](https://avatars.githubusercontent.com/u/480334?v=4&s=39 "Jacob Foshee")](https://github.com/jfoshee)
[![](https://avatars.githubusercontent.com/u/33566379?u=bf62e2b46435a267fa246a64537870fd2449410f&v=4&s=39 "")](https://github.com/Mrxx99)
[![Eric Johnson](https://avatars.githubusercontent.com/u/26369281?u=41b560c2bc493149b32d384b960e0948c78767ab&v=4&s=39 "Eric Johnson")](https://github.com/eajhnsn1)
[![Jonathan ](https://avatars.githubusercontent.com/u/5510103?u=98dcfbef3f32de629d30f1f418a095bf09e14891&v=4&s=39 "Jonathan ")](https://github.com/Jonathan-Hickey)
[![Ken Bonny](https://avatars.githubusercontent.com/u/6417376?u=569af445b6f387917029ffb5129e9cf9f6f68421&v=4&s=39 "Ken Bonny")](https://github.com/KenBonny)
[![Simon Cropp](https://avatars.githubusercontent.com/u/122666?v=4&s=39 "Simon Cropp")](https://github.com/SimonCropp)
[![agileworks-eu](https://avatars.githubusercontent.com/u/5989304?v=4&s=39 "agileworks-eu")](https://github.com/agileworks-eu)
[![Zheyu Shen](https://avatars.githubusercontent.com/u/4067473?v=4&s=39 "Zheyu Shen")](https://github.com/arsdragonfly)
[![Vezel](https://avatars.githubusercontent.com/u/87844133?v=4&s=39 "Vezel")](https://github.com/vezel-dev)
[![ChilliCream](https://avatars.githubusercontent.com/u/16239022?v=4&s=39 "ChilliCream")](https://github.com/ChilliCream)
[![4OTC](https://avatars.githubusercontent.com/u/68428092?v=4&s=39 "4OTC")](https://github.com/4OTC)
[![domischell](https://avatars.githubusercontent.com/u/66068846?u=0a5c5e2e7d90f15ea657bc660f175605935c5bea&v=4&s=39 "domischell")](https://github.com/DominicSchell)
[![Adrian Alonso](https://avatars.githubusercontent.com/u/2027083?u=129cf516d99f5cb2fd0f4a0787a069f3446b7522&v=4&s=39 "Adrian Alonso")](https://github.com/adalon)
[![torutek](https://avatars.githubusercontent.com/u/33917059?v=4&s=39 "torutek")](https://github.com/torutek)
[![Ryan McCaffery](https://avatars.githubusercontent.com/u/16667079?u=c0daa64bb5c1b572130e05ae2b6f609ecc912d4d&v=4&s=39 "Ryan McCaffery")](https://github.com/mccaffers)
[![Seika Logiciel](https://avatars.githubusercontent.com/u/2564602?v=4&s=39 "Seika Logiciel")](https://github.com/SeikaLogiciel)
[![Andrew Grant](https://avatars.githubusercontent.com/devlooped-user?s=39 "Andrew Grant")](https://github.com/wizardness)
[![eska-gmbh](https://avatars.githubusercontent.com/devlooped-team?s=39 "eska-gmbh")](https://github.com/eska-gmbh)
[![Geodata AS](https://avatars.githubusercontent.com/u/5946299?v=4&s=39 "Geodata AS")](https://github.com/geodata-no)


<!-- sponsors.md -->
[![Sponsor this project](https://avatars.githubusercontent.com/devlooped-sponsor?s=118 "Sponsor this project")](https://github.com/sponsors/devlooped)

[Learn more about GitHub Sponsors](https://github.com/sponsors)

<!-- https://github.com/devlooped/sponsors/raw/main/footer.md -->
