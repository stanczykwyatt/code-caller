<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="">
</p>

<h1 align="center">Code Caller</h1>

<p align="center">
  Copy your newest 2FA code from Apple Mail with one shortcut.
</p>

<p align="center">
  <a href="https://github.com/stanczykwyatt/code-caller/actions/workflows/ci.yml"><img src="https://github.com/stanczykwyatt/code-caller/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <img src="https://img.shields.io/badge/macOS-14%2B-111?logo=apple" alt="macOS 14 or later">
  <img src="https://img.shields.io/badge/Swift-6-F05138?logo=swift&logoColor=white" alt="Swift 6">
  <img src="https://img.shields.io/badge/network%20access-none-2ea44f" alt="No network access">
  <img src="https://img.shields.io/badge/dependencies-0-2ea44f" alt="No dependencies">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/stanczykwyatt/code-caller" alt="MIT License"></a>
</p>

---

Tired of digging through your emails for a 2FA code every five seconds? Use Code Caller! It's built for macOS, and because it's open source, you can rest easy knowing your 2FA codes are never accessible to anyone but you. Setup is easy: download Code Caller and add your email accounts to Apple Mail. When you press the hotkey, Code Caller finds the most recent code and copies it to your clipboard.

## How it works

1. Add your email accounts to Apple Mail, as many as you like.
2. Press **⌃⇧C** in any app.
3. Code Caller finds the newest code in your inbox, copies it and shows where it came from.
4. Paste with **⌘V**. The clipboard clears itself a minute later.

If the code hasn't arrived yet, Code Caller asks Mail to check for new messages and looks again for a few seconds.

## Privacy and security

Code Caller is built so it *can't* misuse your data, not just so it promises not to.

- **No internet access.** The app runs in the macOS App Sandbox without a network entitlement, so it can't send anything anywhere. CI checks every build for this.
- **Only reads Mail when you press the shortcut,** and only the last 10 minutes of your inbox. Nothing runs in the background.
- **Saves nothing it reads.** No logs, no history, no copies of your email.
- **Minimal permissions.** It only asks to control Mail. The shortcut works without Accessibility or Input Monitoring access, so it never sees what you type.
- **Careful with the clipboard.** Codes stay off Universal Clipboard, are hidden from clipboard managers and are cleared automatically.
- **No dependencies.** Every line that runs is in this repository.

You can check the app's permissions yourself:

```bash
codesign -d --entitlements - "/Applications/Code Caller.app"
```

The full details are in [SECURITY.md](SECURITY.md).

## Install

### Download

Get the latest version from [Releases](https://github.com/stanczykwyatt/code-caller/releases) and move it to your Applications folder.

> **About the security warning:** The first time you open Code Caller, macOS may say Apple couldn't verify that it's free of malware. That's not because anything is wrong with the app. Apple charges developers $99 a year to verify their apps, and Code Caller hasn't paid that fee yet. The code is fully open source, so you can read every line or build the app yourself. To open it, go to **System Settings → Privacy & Security** and click **Open Anyway**.

### Build from source

You need macOS 14 or later and Swift 6, which comes with Xcode 16 or the Command Line Tools.

```bash
git clone https://github.com/stanczykwyatt/code-caller.git
cd code-caller
scripts/build-app.sh
open "build/Code Caller.app"
```

## Settings

Open Settings from the menu bar icon to:

- change the shortcut
- choose how far back to look (5 to 30 minutes)
- choose when the clipboard clears, or turn clearing off
- hide the code in the confirmation notice
- open Code Caller at login

## What codes it finds

Code Caller reads codes from verification and sign-in emails in English, Spanish, French, German, Chinese, Japanese, Korean and more. It handles six-digit codes, codes split into groups like `482 913`, and letter-and-number codes like `F4K9P` or `XKF-7PQ`. It skips prices, dates, phone numbers, order numbers and links.

If it misses a code or copies the wrong one, please [open an issue](https://github.com/stanczykwyatt/code-caller/issues/new/choose). Remove the real code and personal details first.

## Roadmap

- Codes from password managers with official command-line tools, such as 1Password, Bitwarden and KeePassXC
- A built-in authenticator, with secrets kept in the macOS Keychain behind Touch ID
- Codes from Messages, as an opt-in feature

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
