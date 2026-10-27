import AppKit
import SwiftUI

enum Confirmation {
    case looking
    case copied(code: String?, sender: String, received: Date)
    case nothingFound(minutes: Int)
    case needsMailAccess
    case mailMissing
    case failed
}

/// A small notice at the top of the screen that says what happened.
///
/// It never takes focus or blocks clicks, and asks macOS to keep it out of
/// screenshots and screen sharing so a code isn't shown to an audience.
@MainActor
final class ConfirmationPanel {
    private let panel = NSPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
    private let background = NSVisualEffectView()
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

        background.material = .popover
        background.blendingMode = .behindWindow
        background.state = .active
        background.maskImage = Self.roundedMask(radius: 14)
        panel.contentView = background
    }

    func show(_ confirmation: Confirmation) {
        let content = NSHostingView(rootView: ConfirmationView(confirmation: confirmation))
        let size = content.fittingSize
        content.frame = NSRect(origin: .zero, size: size)
        background.subviews.forEach { $0.removeFromSuperview() }
        background.addSubview(content)

        let screen = NSScreen.screens.first { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) } ?? NSScreen.main
        if let visible = screen?.visibleFrame {
            let origin = NSPoint(x: visible.midX - size.width / 2, y: visible.maxY - size.height - 12)
            panel.setFrame(NSRect(origin: origin, size: size), display: true)
        }
        panel.invalidateShadow()

        hideTask?.cancel()
        if !panel.isVisible {
            panel.alphaValue = 0
            panel.orderFrontRegardless()
        }
        NSAnimationContext.runAnimationGroup { context in
            context.duration = 0.15
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

    private static func roundedMask(radius: CGFloat) -> NSImage {
        let edge = radius * 2 + 1
        let image = NSImage(size: NSSize(width: edge, height: edge), flipped: false) { rect in
            NSColor.black.setFill()
            NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius).fill()
            return true
        }
        image.capInsets = NSEdgeInsets(top: radius, left: radius, bottom: radius, right: radius)
        image.resizingMode = .stretch
        return image
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
        case .copied(let code, _, _): code ?? "Code copied"
        case .nothingFound: "No code found"
        case .needsMailAccess: "Mail access is off"
        case .mailMissing: "Mail isn't installed"
        case .failed: "Couldn't reach Mail"
        }
    }

    var detail: String? {
        switch self {
        case .looking: nil
        case .copied(let code, let sender, let received):
            "\(code == nil ? "From" : "Copied from") \(sender) · \(Self.age(of: received))"
        case .nothingFound(let minutes): "Nothing in the last \(minutes) minutes of Mail"
        case .needsMailAccess: "Allow it in Code Caller Settings"
        case .mailMissing: "Code Caller reads codes from Apple Mail"
        case .failed: "Try again in a moment"
        }
    }

    var showsCode: Bool {
        if case .copied(.some, _, _) = self { true } else { false }
    }

    /// How long the notice stays up. Nil while still looking.
    var duration: Double? {
        switch self {
        case .looking: nil
        case .copied: 2
        default: 3.5
        }
    }

    fileprivate var symbol: String {
        switch self {
        case .looking, .copied: "checkmark.circle.fill"
        case .nothingFound: "magnifyingglass"
        case .needsMailAccess: "lock.fill"
        case .mailMissing: "envelope"
        case .failed: "exclamationmark.triangle.fill"
        }
    }

    fileprivate var tint: Color {
        switch self {
        case .copied: .green
        case .failed, .needsMailAccess: .orange
        default: .secondary
        }
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
