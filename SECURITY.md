# Security

## Supported versions

Only the latest release of each package gets fixes: `Digitals.Analyzers` and `Digitals.Testing`. A fix ships as a new release, never as a patch to an old one.

## Reporting a vulnerability

Report it privately at [Security > Report a vulnerability](https://github.com/Digitals-OYT-Ltd/csharp-conventions/security/advisories/new). Don't open a public issue. Say which package and version, and how to reproduce it.

That covers the packages and everything they bring: the `DT#####` analyzers and the coverage gate, which run inside the compiler and the test host on every developer and CI machine, and the bundled packages, SonarAnalyzer.CSharp, Roslynator, the VS Threading analyzers, BannedApiAnalyzers, xunit.v3, NSubstitute, FakeTimeProvider and the Microsoft.Testing extensions. A vulnerability in a bundled package that its own project has already fixed is something we only need to ship, and `.github/workflows/security.yml` looks for those every Monday and opens an issue.

## Withdrawing a vulnerable version

nuget.org can't delete a version, and a pinned version restores whatever we do. So we tell everyone on it, and make sure nothing new picks it up. In order:

1. Release the fix: tag `analyzers/vX.Y.Z` or `testing/vX.Y.Z` as usual.
2. Publish a GitHub security advisory from the report, or from [a new draft](https://github.com/Digitals-OYT-Ltd/csharp-conventions/security/advisories/new). Ecosystem NuGet, the package `Digitals.Analyzers` or `Digitals.Testing`, the affected range such as `< 1.4.2`, and the patched version. Request a CVE there too. Once GitHub reviews it, it reaches the GitHub Advisory Database and nuget.org shows the version as vulnerable.
3. Deprecate the affected versions on nuget.org: the package's Manage page, Deprecation, reason Critical bugs, and the message `Upgrade to X.Y.Z: <advisory URL>`. This shows at once, without waiting on the review.
4. Unlist them on the same Manage page, Listing, so a new reference and a floating version skip them.

Step 2 is what consumers see. Their next restore audits the reference and reports one of `NU1901` to `NU1904`, which fails it under `TreatWarningsAsErrors`. `DIGB004` makes sure no repo on the package has weakened that audit.
