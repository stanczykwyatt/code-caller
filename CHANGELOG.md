# Changelog

## 0.2.0 (2026-10-27)

Codes from texts and authenticator apps.

- **Messages:** finds codes in texts and iMessages too, including passcodes texted by Duo and banks. It's off until you turn it on and give Code Caller Full Disk Access, and the sandbox still blocks every other file.
- **Authenticator:** a built-in authenticator for time-based codes. Press ⌃⇧A, use Touch ID, type a few letters and press Return to copy a code.
- **Imports:** bring accounts over from Apple Passwords, Google Authenticator, 1Password, Bitwarden, Aegis, 2FAS and andOTP, or from QR code screenshots, `otpauth://` links and setup keys.
- **Source in the notice:** shows where each code came from, like "Copied from GitHub · Mail · just now", and slides in from under the menu bar.
- **Mail's Junk folder:** now checked too.
- **Promo codes:** codes like `SAVE20 at checkout` are no longer mistaken for verification codes.
- **Settings:** split into General and Authenticator tabs. Mail and Messages can each be turned on or off.
- **Testing:** stress tests feed thousands of damaged inputs through every parser.
- **Hardening from a full code review:**
  - The authenticator search closes as soon as the vault locks.
  - "Every time" really asks every time.
  - Links with apostrophes or commas import correctly, and junk setup keys are refused.
  - Everyday texts like "zip code 94110" are no longer copied.
  - Shortcuts can't take over ⌘C or ⌘V.
  - Messages access is limited to its database files.

## 0.1.0 (2026-10-27)

The first version. Runs on macOS 14 or later, on Apple silicon and Intel Macs.

- Copy your newest verification code from Apple Mail with one shortcut (⌃⇧C by default).
- Finds numeric, grouped and letter-and-number codes in English, Spanish, French, German, Chinese, Japanese and Korean emails.
- Fetches new mail and looks again when a code hasn't arrived yet.
- Keeps codes off Universal Clipboard and out of clipboard managers, and clears them after a minute.
- Shows a notice with the code and its sender that stays out of screenshots and screen sharing.
- Settings for the shortcut, how far back to look, clipboard clearing, and opening at login.
- Runs in the App Sandbox with no network access.
