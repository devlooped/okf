# OKF sample bundles

Generated graph files (`okf.json`, `graph.json`) include
`"$schema": "https://www.schemastore.org/okf-0.2.json"` so editors can validate
against the published OKF graph format. Use `okf schema -v 0.1` for the
previous graph shape.

`samples/the-law` is a literary corpus bumped to `okf_version: "0.2"` with
`generated: { by: human:kzu, at: … }` in place of `timestamp`.

`samples/attested` is a compact OKF v0.2 Appendix A bundle: a narrative
Metric, two Attested Computations (human-reviewed + fresh vs
machine-confirmed + stale), and a v0.1-shaped concept that still uses
`timestamp` plus `# Citations`.

Run the checker against the failure showcase bundle:

```bash
okf check samples/check-failures
```

This bundle is **intentionally invalid**. Each file triggers a specific diagnostic.

| File | Expected error |
|------|----------------|
| `concepts/missing-frontmatter.md` | Missing YAML frontmatter block |
| `concepts/unterminated-frontmatter.md` | Unterminated YAML frontmatter block |
| `concepts/invalid-yaml.md` | Unclosed flow sequence in frontmatter |
| `concepts/flow-mapping-unclosed.md` | Unclosed flow mapping (`{ …`) in frontmatter |
| `concepts/unclosed-quote.md` | Unclosed double-quoted scalar |
| `concepts/unclosed-single-quote.md` | Unclosed single-quoted scalar |
| `concepts/bad-indent.md` | Mis-indented mapping key |
| `concepts/block-scalar-indent.md` | Under-indented literal block scalar line |
| `concepts/sequence-root.md` | Sequence entry mixed into a mapping |
| `concepts/undefined-anchor.md` | Reference to an undefined YAML alias |
| `concepts/missing-type.md` | Missing non-empty `type` field |
| `concepts/empty-type.md` | Missing non-empty `type` field |
| `concepts/broken-relative-link.md` | Broken relative link |
| `concepts/broken-absolute-link.md` | Broken bundle-rooted (`/…`) link |
| `concepts/sources-missing-resource.md` | `sources` entry missing `resource` (warning) |
| `concepts/bad-status.md` | `status` not draft/stable/deprecated (warning) |
| `concepts/orphan-footnote.md` | `[^id]` with no matching `sources[].id` (warning) |
| `concepts/bad-path-field.md` | Unresolved path-valued frontmatter field (warning) |
| `index.md` | Unexpected root index frontmatter keys; missing section; missing list entry; broken link |
| `bad-index/frontmatter/index.md` | index.md must not contain frontmatter |
| `bad-index/malformed-entry/index.md` | Index entry format |
| `bad-index/broken-link/index.md` | Broken link in index |
| `log.md` | Frontmatter forbidden; invalid date; non-ISO heading; non-list entry; broken link |
| `empty-log/log.md` | Missing date heading |

External URLs (for example `https://example.com`) are ignored and do not fail the check.
