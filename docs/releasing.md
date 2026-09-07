# Releasing

For maintainers. The three packages share one version and are always released together.

## How a version is decided

[MinVer](https://github.com/adamralph/minver) reads it from git, so there is no version number
anywhere in the repository to forget to bump:

| Commit | Version |
|---|---|
| tagged `v1.2.3` | `1.2.3` |
| anything else | the next patch as `1.2.4-preview.0.N` |

That is why the release job checks out with `fetch-depth: 0` and why the workflow does **not** pass
`/p:Version` — it would fight MinVer. A "Verify the packed version matches the tag" step fails the
build if the two ever disagree.

## Cutting a release

1. Move every entry under `## Unreleased` in [`CHANGELOG.md`](../CHANGELOG.md) into a new version
   heading, and commit it to `main`.
2. Promote the public API: append the contents of each project's `PublicAPI.Unshipped.txt` to its
   `PublicAPI.Shipped.txt` and empty the unshipped file. From then on the analyzer reports a
   removal or a signature change as a build error rather than letting it ship silently.
3. Tag and push:

   ```bash
   git tag v1.2.3
   git push origin v1.2.3
   ```

   The tag triggers `.github/workflows/release.yml`. `workflow_dispatch` with the tag name as input
   does the same thing by hand.

4. After the **first** ever release, set `PackageValidationBaselineVersion` to it in each of the
   three package files. Package validation then diffs every later build against the published
   baseline and fails on a break.

## What the release workflow does

| Job | |
|---|---|
| `test` | builds Release and runs the full suite; nothing is published if it fails |
| `build` | refuses a tag whose commit is not on `origin/main`, packs, attests build provenance with [`actions/attest-build-provenance`](https://github.com/actions/attest-build-provenance), and uploads the `.nupkg`/`.snupkg` as an artifact |
| `release` | pushes to GitHub Packages, then to nuget.org |

Provenance attestation means a consumer can verify that a given `.nupkg` was built by this
workflow, from this repository, at that commit.

## Authentication

There are **no long-lived NuGet API keys in this repository** — no secret to leak, rotate or
expire. Two mechanisms, both short-lived:

- **GitHub Packages** takes the workflow's own `GITHUB_TOKEN`, scoped by `packages: write`.
- **nuget.org uses trusted publishing.** `permissions: id-token: write` lets the job mint an OIDC
  token; [`NuGet/login@v1`](https://github.com/NuGet/login) exchanges it with nuget.org for a
  short-lived API key that exists only for that run. This is the same arrangement as
  [TelnetNegotiationCore](https://github.com/HarryCordewener/TelnetNegotiationCore).

### One-time setup on nuget.org

Trusted publishing has to be authorised on the nuget.org side before the first release, and the
policy names *this* repository — a policy for another repository will not do:

1. Sign in to nuget.org as `harrycordewener` (the `user:` in the workflow's `NuGet/login` step).
2. Go to **Account settings → Trusted Publishing** and add a policy for:
   - repository owner `SharpMUSH`
   - repository `MarkupString`
   - workflow file `release.yml`
3. Run the release. If `NuGet/login` fails, the policy details do not match what the workflow
   actually presents — check the owner, repository and workflow filename before anything else.

Package IDs `MarkupString`, `MarkupString.Ansi` and `MarkupString.Html` were unregistered when this
repository was set up; the first successful push claims them.

## Checklist

- [ ] `CHANGELOG.md` updated and merged to `main`
- [ ] `PublicAPI.Unshipped.txt` promoted to `PublicAPI.Shipped.txt` in all three packages
- [ ] Trusted publishing policy exists on nuget.org for `SharpMUSH/MarkupString` (first release only)
- [ ] Tag pushed, `Release` workflow green
- [ ] `PackageValidationBaselineVersion` set (after the first release only)
