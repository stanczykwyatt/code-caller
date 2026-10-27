import AppKit
import SwiftUI

enum Confirmation {
    case looking
    /// `received` is nil for codes that weren't delivered, like ones from the authenticator.
    case copied(code: String?, sender: String, source: CodeSource, received: Date?)
    /// `places` names what was searched, like "Mail and Messages".
    case nothingFound(minutes: Int, places: String)
    case needsMailAccess
    case needsMessagesAccess
    case messagesFailed
    case mailMissing
    case noSources
    case failed
}

/// A small notice at the top of the screen that says what happened.
///
/// It never takes focus or blocks clicks, and asks macOS to keep it out of
/// screenshots and screen sharing so a code isn't shown to an audience.
@MainActor
final class ConfirmationPanel {
    private let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
    private let background = NSVisualEffectView.panelBackground(cornerRadius: 14)
    private var hideTask: Task<Void, Never>?

    init() {
        panel.isFloatingPanel = true
        panel.level = .statusBar
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.sharingType = .none
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.contentView = background
    }

    func show(_ confirmation: Confirmation) {
        let content = NSHostingView(rootView: ConfirmationView(confirmation: confirmation))
        let size = content.fittingSize
        content.frame = NSRect(origin: .zero, size: size)
        background.subviews.forEach { $0.removeFromSuperview() }
        background.addSubview(content)

        guard let visible = NSScreen.withMouse?.visibleFrame else { return }
        let frame = NSRect(
            origin: NSPoint(x: visible.midX - size.width / 2, y: visible.maxY - size.height - 12),
            size: size
        )

        hideTask?.cancel()
        if panel.isVisible {
            panel.setFrame(frame, display: true)
        } else {
            // Slides down from under the menu bar as it fades in, unless Reduce Motion is on.
            let slides = !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
            panel.setFrame(slides ? frame.offsetBy(dx: 0, dy: 10) : frame, display: true)
            panel.alphaValue = 0
            panel.orderFrontRegardless()
        }
        panel.invalidateShadow()
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.2
            context.timingFunction = CAMediaTimingFunction(name: .easeOut)
            panel.animator().setFrame(frame, display: true)
            panel.animator().alphaValue = 1
        }

        guard let duration = confirmation.duration else { return }
        hideTask = Task {
            try? await Task.sleep(for: .seconds(duration))
            guard !Task.isCancelled else { return }
            hide()
        }
    }

    func hide() {
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.25
            panel.animator().alphaValue = 0
        }
        hideTask = Task {
            try? await Task.sleep(for: .milliseconds(300))
            guard !Task.isCancelled else { return }
            panel.orderOut(nil)
        }
    }
}

private struct ConfirmationView: View {
    let confirmation: Confirmation

    var body: some View {
        HStack(spacing: 12) {
            Group {
                if case .looking = confirmation {
                    ProgressView().controlSize(.small)
                } else {
                    Image(systemName: confirmation.symbol)
                        .font(.system(size: 22, weight: .semibold))
                        .foregroundStyle(confirmation.tint)
                }
            }
            .frame(width: 26)
            VStack(alignment: .leading, spacing: 2) {
                Text(confirmation.title)
                    .font(confirmation.showsCode ? .system(size: 22, weight: .semibold, design: .monospaced) : .headline)
                if let detail = confirmation.detail {
                    Text(detail)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
            }
        }
        .padding(.horizontal, 18)
        .padding(.vertical, 12)
        .frame(minWidth: 220, alignment: .leading)
        .fixedSize()
    }
}

extension Confirmation {
    var title: String {
        switch self {
        case .looking: "Looking for a code…"
        case .copied(let code, _, _, _): code ?? "Code copied"
        case .nothingFound: "No code found"
        case .needsMailAccess: "Mail access is off"
        case .needsMessagesAccess: "Messages access is off"
        case .messagesFailed: "Couldn't read Messages"
        case .mailMissing: "Mail isn't installed"
        case .noSources: "Nowhere to look"
        case .failed: "Couldn't reach Mail"
        }
    }

    var detail: String? {
        switch self {
        case .looking: nil
        case .copied(let code, let sender, let source, let received):
            Self.copiedDetail(showsCode: code != nil, sender: sender, source: source, received: received)
        case .nothingFound(let minutes, let places): "Nothing in the last \(minutes) minutes of \(places)"
        case .needsMailAccess: "Allow it in Code Caller Settings"
        case .needsMessagesAccess: "Turn on Full Disk Access in Code Caller Settings"
        case .messagesFailed: "A macOS update may have changed Messages. Check for a Code Caller update."
        case .mailMissing: "Code Caller reads codes from Apple Mail"
        case .noSources: "Turn on Mail or Messages in Code Caller Settings"
        case .failed: "Try again in a moment"
        }
    }

    var showsCode: Bool {
        if case .copied(.some, _, _, _) = self { true } else { false }
    }

    /// How long the notice stays up. Nil while still looking.
    var duration: Double? {
        switch self {
        case .looking: nil
        case .copied: 2.5
        default: 3.5
        }
    }

    fileprivate var symbol: String {
        switch self {
        case .looking: "checkmark.circle.fill"
        case .copied(_, _, let source, _): source.symbol
        case .nothingFound, .noSources: "magnifyingglass"
        case .needsMailAccess, .needsMessagesAccess: "lock.fill"
        case .mailMissing: "envelope"
        case .failed, .messagesFailed: "exclamationmark.triangle.fill"
        }
    }

    fileprivate var tint: Color {
        switch self {
        case .copied(_, _, let source, _): source.tint
        case .failed, .messagesFailed, .needsMailAccess, .needsMessagesAccess: .orange
        default: .secondary
        }
    }

    /// For example "Copied from GitHub · Mail · just now".
    private static func copiedDetail(showsCode: Bool, sender: String, source: CodeSource, received: Date?) -> String {
        var parts = ["\(showsCode ? "Copied from" : "From") \(sender)", source.name]
        if let received {
            parts.append(age(of: received))
        }
        return parts.joined(separator: " · ")
    }

    static func age(of date: Date) -> String {
        let minutes = Int(Date.now.timeIntervalSince(date) / 60)
        switch minutes {
        case ..<1: return "just now"
        case 1: return "1 min ago"
        default: return "\(minutes) min ago"
        }
    }
}
