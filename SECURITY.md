# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please create a [private GitHub security advisory](https://github.com/lemonlion/InMemoryEmulator.Spanner/security/advisories/new) with the following information:

- Description of the vulnerability
- Steps to reproduce
- Potential impact
- Suggested fix (if any)

## Response Timeline

- **Acknowledgment**: Within 48 hours of report
- **Assessment**: Within 7 days
- **Fix & Disclosure**: Within 90 days (responsible disclosure window)

## Scope

This security policy applies to the latest released version of the `InMemoryEmulator.Spanner` NuGet package.

Note: This package is an **in-process test fake** and is not intended for production use. It does not implement authentication, authorization, or encryption. Security reports should focus on vulnerabilities that could affect developers using this package in their test suites (e.g., dependency vulnerabilities, code injection via test data).
