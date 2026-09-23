## Git Commits

When creating commits for this repository:

- Write only the commit subject and body the user would expect.
- **Never** append trailing text attributing an agent as a co-author.
- **Never** add `Co-authored-by:` trailers (or similar) for Cursor, Copilot, Claude, or any other AI assistant.
- **Never** add lines such as `Signed-off-by:` or other footers that credit the agent unless the user explicitly asks for them.

If the user asks for a commit, keep the message clean and human-authored in tone and attribution.

## Packaging

`src/okf/okf.csproj` is a hybrid Native AOT / CoreCLR tool. The SDK packs it; do not bring NuGetizer back.

- `dotnet pack` (no `-r`) writes the pointer package `okf`. It lists every entry in `RuntimeIdentifiers` and contains no implementation.
- `dotnet pack -r any` writes framework-dependent `okf.any` (`PublishAot` is false for that RID).
- `dotnet pack -r <rid>` writes Native AOT `okf.<rid>`.
- CI (`.github/workflows/build.yml`) packs with `-p:RuntimeIdentifiers=any` plus `-r any` only, so the CI feed runs on the fallback and does not advertise unpublished native RIDs.
- Release (`.github/workflows/publish.yml`) packs every RID, including musl inside the Alpine AOT SDK image, then `any`, then the pointer. Push RID and `any` packages before the pointer.
