# Copy 2FA for Windows

Copy your newest 2FA code from Outlook or Thunderbird with one shortcut, or from the built-in authenticator with another.

This is the Windows version of [Copy 2FA](../README.md). It finds codes with the same rules as the macOS app, so both pick the same code from the same email.

## How it works

1. Copy 2FA sits in the system tray, next to the clock.
2. Press **Ctrl+Shift+C** in any app.
3. Copy 2FA finds the newest code in your inbox or junk folder, copies it and shows where it came from, like *Copied from Outlook · GitHub · just now*.
4. Paste with **Ctrl+V**. The clipboard clears itself a minute later.

If the code hasn't arrived yet, Copy 2FA looks again a few times over about six seconds.

For accounts that use an authenticator app, press **Ctrl+Shift+A**, confirm it's you with Windows Hello, type a few letters of the account's name and press **Enter**.

## What works

- **Outlook (classic)**, the desktop Outlook that comes with Microsoft 365 and Office. Copy 2FA reads the inbox and junk folder of every account in it.
- **Thunderbird.** Copy 2FA reads the inbox and junk folders straight from your Thunderbird profile, for IMAP, POP and local accounts.
- **A built-in authenticator** for time-based codes (TOTP), with SHA1, SHA256 or SHA512, 6 to 8 digits and any period. You can import accounts from:
  - a text file of `otpauth://` links, or a single link pasted into Settings
  - Google Authenticator's *Transfer accounts* export (the `otpauth-migration://` link in its QR code)
  - a Safari or Apple Passwords CSV export (passwords in the file are skipped and never kept)
  - an unencrypted Bitwarden JSON export
  - an unencrypted Aegis export
  - a 2FAS backup saved without a password
- Codes in English, Spanish, French, German, Chinese, Japanese, Korean and more, including grouped codes like `482 913` and letter-and-number codes like `F4K9P`.

## What doesn't

- **Text messages.** Phone Link has no API that other apps can use, so codes sent by SMS can't be read.
- **The new Outlook for Windows**, and mail you only read in a browser, such as Outlook.com or Gmail on the web. The new Outlook has no way for other apps to read it. Classic Outlook or Thunderbird, set up with the same accounts, works.
- **Thunderbird folders that aren't kept offline.** For IMAP accounts, *Keep messages for this account on this computer* needs to stay on in Thunderbird's account settings. It's on by default. Thunderbird's optional maildir storage isn't supported either.
- **Counter-based (HOTP) and Steam codes** in the authenticator. They're skipped when importing.
- **Encrypted exports.** Export from Bitwarden, Aegis or 2FAS without encryption, import the file, then delete it.

## Privacy and security

- **No internet access.** There's no networking code in Copy 2FA at all, and CI fails the build if any appears. It sends nothing anywhere and has no telemetry.
- **Only reads your mail when you press the shortcut,** and only the last 10 minutes of it. Nothing runs in the background.
- **Saves nothing it reads.** Messages are kept in memory just long enough to find a code. They're never logged or written to disk.
- **Careful with the clipboard.** Codes are marked so Windows keeps them out of clipboard history (Win+V) and off the cloud clipboard, and so clipboard managers that honor it skip them. The clipboard is cleared after a minute unless you've copied something else since, and again when Copy 2FA quits.
- **Kept off screen.** The notice that shows the code, and the authenticator's account list, are excluded from screenshots, recordings and screen sharing. You can also hide the code in Settings.
- **Authenticator secrets are encrypted** with Windows' Data Protection API, so only your Windows account on this PC can decrypt them, and Windows Hello has to confirm it's you before they're decrypted. They're wiped from memory when the authenticator window closes.
- **The shortcuts only see themselves.** They're registered with `RegisterHotKey`, so Windows only tells Copy 2FA when that exact key combination is pressed. It never sees anything else you type.
- **No dependencies.** Everything that runs is in this repository or part of .NET and Windows.

### Limits worth knowing

- While a code is on the clipboard, any app you run can read it, as with anything you copy.
- Anyone using your unlocked PC could read your email anyway. Copy 2FA doesn't change that.
- Classic Outlook may ask whether to allow another program to read your mail if your antivirus is turned off or out of date. That prompt comes from Outlook's own security check.
- If classic Outlook isn't open, Copy 2FA starts it in the background so it can read your inbox.

## Install

Download `Copy2FA.exe` for your PC from [Releases](https://github.com/stanczykwyatt/copy-2fa/releases) (x64 for most PCs, Arm64 for Snapdragon and other Arm PCs), put it somewhere it can stay, such as a folder in your user directory, and run it. Nothing else needs to be installed.

> **About the SmartScreen warning:** The first time you run Copy 2FA, Windows may say *Windows protected your PC*. That's not because anything is wrong with the app. Copy 2FA isn't code-signed, because a code signing certificate costs money and Copy 2FA hasn't bought one. The source is fully open, so you can read every line or build the app yourself. To run it, click **More info**, then **Run anyway**.

Copy 2FA needs Windows 10 version 2004 or later, or Windows 11. The authenticator needs Windows Hello, and a PIN is enough.

### Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

```powershell
git clone https://github.com/stanczykwyatt/copy-2fa.git
cd copy-2fa
dotnet test windows
dotnet publish windows/Copy2FA -c Release -r win-x64 -o publish
.\publish\Copy2FA.exe
```

Use `-r win-arm64` for an Arm PC. The result is one self-contained `Copy2FA.exe`.

The tests and the shared library build on macOS and Linux too, so code detection and importers can be worked on anywhere. The app itself compiles there as well, but only runs on Windows.

## Settings

Open Settings from the tray icon to:

- change either shortcut
- turn Outlook or Thunderbird on or off
- choose how far back to look (5 to 30 minutes)
- choose when the clipboard clears, or turn clearing off
- hide the code in the notice
- start Copy 2FA with Windows
- unlock the authenticator with Windows Hello to import, add or delete accounts

Settings are saved in `%LOCALAPPDATA%\Copy2FA\settings.json`, with nothing sensitive in them. Authenticator accounts are in `%LOCALAPPDATA%\Copy2FA\vault.bin`, encrypted. To remove Copy 2FA completely, turn off *Start with Windows*, quit it from the tray, then delete that folder and `Copy2FA.exe`.

## Project layout

| | |
|---|---|
| `Copy2FA.Core` | Code detection, the mbox reader, TOTP, importers and settings. Cross-platform. |
| `Copy2FA.Core.Tests` | xunit tests, including every macOS code detection test and the RFC 6238 test vectors. |
| `Copy2FA` | The Windows tray app. |
