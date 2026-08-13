# OKF sample bundles

Generated graph files (`okf.json`, `graph.json`) include
`"$schema": "https://www.schemastore.org/okf-0.1.json"` so editors can validate
against the published OKF graph format.

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
| `index.md` | Unexpected root index frontmatter keys; missing section; missing list entry; broken link |
| `bad-index/frontmatter/index.md` | index.md must not contain frontmatter |
| `bad-index/malformed-entry/index.md` | Index entry format |
| `bad-index/broken-link/index.md` | Broken link in index |
| `log.md` | Frontmatter forbidden; invalid date; non-ISO heading; non-list entry; broken link |
| `empty-log/log.md` | Missing date heading |

External URLs (for example `https://example.com`) are ignored and do not fail the check.
