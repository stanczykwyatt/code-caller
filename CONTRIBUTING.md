# Contributing

Thanks for helping out! Bug reports, code-detection misses and pull requests are all welcome.

## Getting started

You need macOS 14 or later and Swift 6 (Xcode 16 or the Command Line Tools).

```bash
swift test               # run the tests
scripts/build-app.sh     # build "Copy 2FA.app" into ./build
open "build/Copy 2FA.app"
```

## Reporting a missed or wrong code

Code detection lives in `Sources/Copy2FACore/CodeExtractor.swift`, with tests in `Tests/Copy2FACoreTests`. If a code isn't found, or the wrong number is copied, open an issue with the subject and the text around the code. **Replace the real code and any personal details first.** Even better, add a failing test.

## Ground rules

- **No network access, ever.** Pull requests that add a network entitlement or third-party analytics won't be merged.
- **No new dependencies** without a very good reason. The app has none today, which keeps it easy to audit.
- **Keep permissions minimal.** New features shouldn't need more access than they truly use.
- Run `swift test` before opening a pull request, and add tests for code-detection changes.
- Keep commits small, with a short message saying what changed and why.

## Security issues

Please don't open public issues for vulnerabilities. See [SECURITY.md](SECURITY.md).
