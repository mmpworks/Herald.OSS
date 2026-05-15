# Security policy

## Reporting a vulnerability

**Do not open a public GitHub issue for security vulnerabilities.**

Email **security@mmpworks.com** with:

- A description of the vulnerability and its impact
- Steps to reproduce, with a minimal test case if possible
- The affected version and target framework
- Your name and contact information (optional — anonymous reports are
  accepted)

We will acknowledge receipt within **2 business days** and provide a
substantive response within **10 business days**.

## What we treat as a security issue

Herald.OSS is a structured-logging library. The data flowing through it
is frequently sensitive — credentials, PII, business-confidential
strings. We treat the following as security issues, in addition to the
obvious:

- **Any defect in the redaction fast path** that would allow a
  redacted field to leak in its un-redacted form.
- **Any defect in the kernel fast path** that would allow events to be
  silently dropped, duplicated, or misrouted between sinks.
- **Any defect in the hot-reload flow** that would allow a malformed
  config to leave the pipeline in a half-applied state where security
  policies (redaction rules, level floors, sink routes) are not
  enforced.
- **Any memory-safety issue** in code paths that handle untrusted
  input (template parsing, JSON config deserialisation, log property
  destructuring).
- **Any cryptographic primitive choice** that is not FIPS-approved or
  NIST-recommended, in addons that perform signing or hashing.
- **Any timing, fault-injection, or side-channel attack** that could
  recover secrets from logged content.
- **Any deviation from the AOT/trim safety contract** that would cause
  Herald.OSS to fail at runtime on a published native-AOT app despite
  the analyzer-clean build.

## What we do not treat as a security issue

- Performance regressions (file a regular bug report)
- API ergonomics (file a feature request)
- Documentation errors (file a regular bug report, unless the error
  misrepresents a security property)
- Issues in third-party dependencies unless the vulnerability arises
  from how Herald.OSS uses them
- Behaviours that are explicitly documented and within design intent

## Coordinated disclosure

We follow industry-standard coordinated disclosure:

1. **Day 0** — report received, acknowledged
2. **Day 0–14** — triage, severity assessment, fix development
3. **Day 14–90** — private fix release, embargo period
4. **Day 90+** — public disclosure with CVE assignment, advisory, and
   credit to the reporter

We may shorten the embargo if the vulnerability is being actively
exploited or if the reporter publishes details independently.

## Supported versions

Security fixes are issued for:

- The current minor release on the `main` branch
- The most recent previous minor release

Older releases are not patched. Upgrade is the supported remediation.

## Bug bounty

Herald.OSS does not currently offer a bug bounty. Reporters who wish to
be credited are listed in the security advisory and the release notes.

## Hall of credit

Reporters who choose to be credited will be listed here once the first
public security advisory ships.
