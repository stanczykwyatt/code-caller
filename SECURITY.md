# Security

Code Caller handles one-time codes, so it is built to do as little as possible with as little access as possible.

## Reporting a vulnerability

Please report security problems privately through **Security → Report a vulnerability** on this repository, not in a public issue. You'll get a reply within a few days.

## What Code Caller can access

| | |
|---|---|
| **Apple Mail** | Sends read-only Apple Events to Mail when you press the shortcut. macOS asks you first, and you can turn it off in System Settings → Privacy & Security → Automation. |
| **Clipboard** | Writes the code, then clears it after the delay you choose, unless you've copied something else since. |
| **Its own settings** | Stored in the app's sandbox container. Nothing read from Mail is ever stored. |

## What it can't access

- **The internet.** The app runs in the App Sandbox without a network entitlement, so macOS blocks every connection. CI checks every build for this.
- **Other apps.** Its only Apple Events entitlement is for Mail.
- **Your keystrokes.** The shortcut uses the Carbon hot key API, which only reports that one shortcut. No Accessibility or Input Monitoring permission is needed.
- **Your files.** The sandbox gives the app no file access outside its own container.

Check it yourself on any copy of the app:

```bash
codesign -d --entitlements - "/Applications/Code Caller.app"
```

## How codes are handled

- Mail is only read when you press the shortcut, and only messages from the last few minutes.
- Messages are kept in memory just long enough to find a code. They're never logged or written to disk.
- The only value put into the AppleScript is a number of seconds, so message content can't change what the script does.
- The clipboard copy stays on this Mac (it's kept off Universal Clipboard) and is marked so clipboard managers skip it.
- The notice that shows the code asks macOS to keep it out of screenshots and screen sharing. You can also hide the code in Settings.

## Limits worth knowing

- While a code is on the clipboard, any app you run can read it, as with anything you copy.
- Anyone using your unlocked Mac could read your email anyway. Code Caller doesn't change that.
- Releases aren't notarized yet, because the Apple Developer Program fee hasn't been paid. You can build the app from source instead and compare it with the code.

## Supported versions

Only the latest release gets security fixes.
