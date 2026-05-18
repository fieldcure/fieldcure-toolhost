# Security Policy

## Supported versions

Until v1.0 ships, only the latest minor version receives security fixes. After
v1.0:

- The latest major receives all fixes (security and non-security).
- The previous major receives security fixes for 12 months from the new major's
  release date.

When standalone `dnx` ships and we enter the sunset window described in
[ROADMAP.md](ROADMAP.md), security fixes continue for the documented period.

## Reporting a vulnerability

Please email **security@fieldcure.co** with:

- A description of the vulnerability and its impact.
- Reproduction steps or a proof of concept.
- Your name / handle for credit (optional).

We acknowledge within 3 business days and aim to ship a fix within 30 days for
confirmed high-severity issues. Please do **not** open public GitHub issues for
security reports.

## Package signature policy

`FieldCure.ToolHost` does not verify package signatures itself — it delegates to
`NuGet.Packaging` / `NuGet.Protocol`, which honor the user's NuGet client policy
(`nuget.config`'s `<trustedSigners>` and `<clientCertificates>` sections).

If you require strict signing, configure
[NuGet trusted signers](https://learn.microsoft.com/en-us/nuget/consume-packages/installing-signed-packages)
at the user or machine level. ToolHost will inherit those settings.

## Credentials

ToolHost forwards credentials to NuGet via the standard credential provider
plugin protocol. We do **not** persist credentials anywhere. See
[docs/authenticated-feeds.md](docs/authenticated-feeds.md) for the supported
providers and how they discover secrets.
