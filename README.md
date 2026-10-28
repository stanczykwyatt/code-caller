<p align="center">
  <img src="docs/icon.png" width="128" height="128" alt="">
</p>

<h1 align="center">Copy 2FA</h1>

<p align="center">
  Copy your newest 2FA code from Mail, Messages or your authenticator with one shortcut.
</p>

<p align="center">
  <a href="https://github.com/stanczykwyatt/copy-2fa/actions/workflows/ci.yml"><img src="https://github.com/stanczykwyatt/copy-2fa/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/stanczykwyatt/copy-2fa/releases/latest"><img src="https://img.shields.io/github/v/release/stanczykwyatt/copy-2fa" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/macOS-14%2B-111?logo=apple" alt="macOS 14 or later">
  <img src="https://img.shields.io/badge/Swift-6-F05138?logo=swift&logoColor=white" alt="Swift 6">
  <img src="https://img.shields.io/badge/network%20access-none-2ea44f" alt="No network access">
  <img src="https://img.shields.io/badge/dependencies-0-2ea44f" alt="No dependencies">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/stanczykwyatt/copy-2fa" alt="MIT License"></a>
</p>

---

Tired of digging through your emails and texts for a 2FA code every five seconds? Use Copy 2FA! It's built for macOS, and because it's open source, you can rest easy knowing your 2FA codes are never accessible to anyone but you. Setup is easy: download Copy 2FA and add your email accounts to Apple Mail. When you press the hotkey, Copy 2FA finds the most recent code in Mail or Messages and copies it to your clipboard. Codes from authenticator apps work too: bring your accounts over once, then pick one with a second shortcut.

## How it works

| Press | What happens |
|---|---|
| **⌃⇧C** | Copy 2FA finds the newest code in Mail and Messages, copies it and shows where it came from, like *Copied from GitHub · Mail · just now*. |
| **⌃⇧A** | After Touch ID, a search box opens with your authenticator accounts. Type a few letters and press Return to copy that code. |

Paste with **⌘V**. The clipboard clears itself a minute later. If an emailed code hasn't arrived yet, Copy 2FA asks Mail to check for new messages and looks again for a few seconds.

## Where codes come from

| Source | What it reads | What it needs |
|---|---|---|
| **Mail** | Every account you've added to Apple Mail (iCloud, Gmail, Outlook, Yahoo, Exchange or any other), including Junk | Your OK the first time, when macOS asks |
| **Messages** | Texts and iMessages, including codes sent by Duo, banks and most websites | Full Disk Access, which you turn on yourself. Off by default. |
| **Authenticator** | Accounts you've added to Copy 2FA, stored in your keychain | Touch ID or your Mac's password |

### Bringing your authenticator accounts over

Most apps let you export your accounts, and Copy 2FA can import them. Delete the exported file afterward.

| App | How |
|---|---|
| **Apple Passwords** | In Passwords, choose **File → Export All Passwords**, import the CSV file, then delete it. |
| **Google Authenticator** | Use **Transfer accounts → Export accounts**, take a screenshot of the QR code, then import or paste it. |
| **1Password, Bitwarden** | Export as an unencrypted CSV or JSON file, import it, then delete it. |
| **Aegis, 2FAS, andOTP** | Export an unencrypted backup, import it, then delete it. |
| **Authy** | Authy doesn't allow exports. Turn two-factor authentication off and on again on each website, then scan the new QR code. |
| **Duo** | Duo Push approvals have no code to copy. If Duo texts you passcodes, turn on Messages. |
| **Any website** | Screenshot its QR code and import it, paste its `otpauth://` link, or type the setup key it shows. |

## Privacy and security

Copy 2FA is built so it *can't* misuse your data, not just so it promises not to.

- **No internet access.** The app runs in the macOS App Sandbox without a network entitlement, so it can't send anything anywhere. CI checks every build for this.
- **Only reads when you press a shortcut,** and only the last 10 minutes of Mail and Messages. Nothing runs in the background.
- **Saves nothing it reads.** No logs, no history, no copies of your email or texts.
- **Locked-down file access.** Even with Full Disk Access, the sandbox only lets Copy 2FA read your Messages database and files you pick to import.
- **Authenticator secrets stay in your keychain,** are never synced or exported, need Touch ID to open, and are cleared from memory when the vault locks, your screen locks or your Mac sleeps.
- **Careful with the clipboard.** Codes stay off Universal Clipboard, are hidden from clipboard managers and are cleared automatically. Pasted setup links are cleared right after import.
- **Hidden from screen sharing.** The code notice and the authenticator search ask macOS to keep them out of screenshots and recordings.
- **Minimal permissions.** The shortcuts work without Accessibility or Input Monitoring access, so Copy 2FA never sees what you type.
- **No dependencies.** Every line that runs is in this repository.

You can check the app's permissions yourself:

```bash
codesign -d --entitlements - "/Applications/Copy 2FA.app"
```

The full details are in [SECURITY.md](SECURITY.md).

## Install

### Download

Get the latest version from [Releases](https://github.com/stanczykwyatt/copy-2fa/releases) and move it to your Applications folder.

> **About the security warning:** The first time you open Copy 2FA, macOS may say Apple couldn't verify that it's free of malware. That's not because anything is wrong with the app. Apple charges developers $99 a year to verify their apps, and Copy 2FA hasn't paid that fee yet. The code is fully open source, so you can read every line or build the app yourself. To open it, go to **System Settings → Privacy & Security** and click **Open Anyway**.

### Build from source

You need macOS 14 or later and Swift 6, which comes with Xcode 16 or the Command Line Tools.

```bash
git clone https://github.com/stanczykwyatt/copy-2fa.git
cd copy-2fa
scripts/build-app.sh
open "build/Copy 2FA.app"
```

## Settings

Open Settings from the menu bar icon.

**General:** change the shortcut, choose how far back to look (5 to 30 minutes), turn Mail and Messages on or off, choose when the clipboard clears, hide the code in the notice, and open Copy 2FA at login.

**Authenticator:** change its shortcut, choose how often to ask for Touch ID, and import, add or delete accounts.

## What codes it finds

Copy 2FA reads codes from verification and sign-in messages in English, Spanish, French, German, Chinese, Japanese, Korean and more. It handles six-digit codes, codes split into groups like `482 913`, and letter-and-number codes like `F4K9P` or `XKF-7PQ`. It skips prices, dates, phone numbers, order numbers, links and promo codes like `SAVE20`.

If it misses a code or copies the wrong one, please [open an issue](https://github.com/stanczykwyatt/copy-2fa/issues/new/choose). Remove the real code and personal details first.

## Windows

A Windows version is in preview. It reads codes from Outlook (classic) and Thunderbird, has the same authenticator behind Windows Hello, and uses the same code detection as the Mac app. It hasn't been tested on many PCs yet, so please [report anything odd](https://github.com/stanczykwyatt/copy-2fa/issues/new/choose). See the [Windows README](windows/README.md) to download or build it.

## Roadmap

- Testing the Windows version on more PCs, then a full release
- Codes from password managers' own apps, if they ever offer a local, offline way to share them
- Signed and notarized releases

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE)
