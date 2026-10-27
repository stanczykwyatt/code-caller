# Security

Code Caller handles one-time codes, so it is built to do as little as possible with as little access as possible.

## Reporting a vulnerability

Please report security problems privately through **Security → Report a vulnerability** on this repository, not in a public issue. You'll get a reply within a few days.

## What Code Caller can access

| | |
|---|---|
| **Apple Mail** | Sends read-only Apple Events to Mail when you press the shortcut. macOS asks you first, and you can turn it off in System Settings → Privacy & Security → Automation. |
| **Messages** | Off by default. When you turn it on and give Code Caller Full Disk Access, it opens `~/Library/Messages/chat.db` and its two journal files read-only, and reads the last few minutes of incoming texts. Attachments stay out of reach. |
| **Files you pick** | When you import authenticator accounts, it can read only the one file you choose. |
| **Keychain** | Keeps authenticator secrets in one login keychain item that only Code Caller can read. |
| **Clipboard** | Writes the code, then clears it after the delay you choose, unless you've copied something else since. |
| **Its own settings** | Stored in the app's sandbox container. Nothing read from Mail or Messages is ever stored. |

## What it can't access

- **The internet.** The app runs in the App Sandbox without a network entitlement, so macOS blocks every connection. CI checks every build for this.
- **Other apps.** Its only Apple Events entitlement is for Mail.
- **Your keystrokes.** The shortcuts use the Carbon hot key API, which only reports those shortcuts. No Accessibility or Input Monitoring permission is needed.
- **Your other files.** Even with Full Disk Access turned on, the sandbox blocks every file except the Messages database and files you pick. This was tested by trying to read files in your home folder, Documents, Mail's own folder and Messages' attachments: the sandbox denied all of them.

Check it yourself on any copy of the app:

```bash
codesign -d --entitlements - "/Applications/Code Caller.app"
```

## How codes are handled

- Mail and Messages are only read when you press the shortcut, and only messages from the last few minutes.
- Messages are kept in memory just long enough to find a code. They're never logged or written to disk.
- The only value put into the AppleScript is a number of seconds, and the only value put into the Messages query is a timestamp, bound as a parameter. Message content can't change what either one does.
- Message text from Messages is read straight from its archived bytes. Nothing is unarchived, so a crafted message can't make the app create objects.
- The clipboard copy stays on this Mac (it's kept off Universal Clipboard) and is marked so clipboard managers skip it.
- The code notice and the authenticator search ask macOS to keep them out of screenshots and screen sharing. You can also hide the code in the notice.
- Every parser is tested with thousands of random and damaged inputs, so a malicious email, text or import file can't crash the app.

## How authenticator secrets are handled

- Secrets are stored in the login keychain, never synced to iCloud, and never exported.
- Opening the authenticator needs Touch ID or your Mac's password. By default it asks every time, even if Settings is open.
- Secrets are only loaded into memory while the authenticator is unlocked. It locks again after the time you choose, two minutes after you unlock it in Settings, when Settings closes, when the screen locks and when the Mac sleeps. The account search closes the moment it locks.
- Touch ID is checked by Code Caller before it reads the keychain. The keychain item itself is protected by your login keychain rather than by Touch ID, because tying it to Touch ID needs a paid Apple developer certificate.
- Imports keep only one-time password secrets. Passwords and anything else in an export file are never stored. Code Caller reminds you to delete the export file afterward.
- Setup links you paste are cleared from the clipboard right after they're imported.

## Limits worth knowing

- While a code is on the clipboard, any app you run can read it, as with anything you copy.
- Anyone using your unlocked Mac could read your email and texts anyway. Code Caller doesn't change that.
- Keeping authenticator secrets on the same Mac you sign in from means a stolen, unlocked Mac has both factors. Touch ID and the automatic lock reduce that risk, but don't remove it.
- Releases aren't notarized yet, because the Apple Developer Program fee hasn't been paid. You can build the app from source instead and compare it with the code. Because unsigned builds change identity with every version, macOS may ask again for access to Mail and the keychain after an update.

## Supported versions

Only the latest release gets security fixes.
