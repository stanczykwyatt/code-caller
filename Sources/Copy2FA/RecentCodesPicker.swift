import AppKit
import Carbon.HIToolbox
import Copy2FACore
import Observation
import SwiftUI

/// A Spotlight-style list of the last few codes, for when the newest isn't the one you need.
///
/// Like the authenticator picker, it takes keyboard focus without activating Copy 2FA, so the
/// app you were in stays in front and ⌘V pastes there. The codes are only kept while it's open.
@MainActor
final class RecentCodesPicker: NSObject, NSWindowDelegate {
    var onCopy: (FoundCode) -> Void = { _ in }
    private let model = RecentCodesModel()
    private var panel: KeyPanel?
    private var monitor: Any?
    private var closeTask: Task<Void, Never>?
    private var lockObservers: [(center: NotificationCenter, token: NSObjectProtocol)] = []

    /// Codes don't stay on screen if the list is left open.
    private static let openSeconds = 60

    var isShown: Bool { panel != nil }

    func show(_ codes: [FoundCode], showsCodes: Bool, lookbackMinutes: Int) {
        close()
        model.codes = codes
        model.selection = 0
        model.showsCodes = showsCodes
        model.lookbackMinutes = lookbackMinutes

        let panel = KeyPanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isFloatingPanel = true
        panel.level = .modalPanel
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.sharingType = .none
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .transient, .ignoresCycle]
        panel.delegate = self

        let background = NSVisualEffectView.panelBackground(cornerRadius: 16)
        let content = NSHostingView(rootView: RecentCodesView(model: model, copy: { [weak self] code in self?.onCopy(code) }))
        content.translatesAutoresizingMaskIntoConstraints = false
        background.addSubview(content)
        NSLayoutConstraint.activate([
            content.leadingAnchor.constraint(equalTo: background.leadingAnchor),
            content.trailingAnchor.constraint(equalTo: background.trailingAnchor),
            content.topAnchor.constraint(equalTo: background.topAnchor),
            content.bottomAnchor.constraint(equalTo: background.bottomAnchor),
        ])
        panel.contentView = background

        let size = content.fittingSize
        if let visible = NSScreen.withMouse?.visibleFrame {
            let origin = NSPoint(x: visible.midX - size.width / 2, y: visible.minY + visible.height * 0.72 - size.height)
            panel.setFrame(NSRect(origin: origin, size: size), display: true)
        }
        self.panel = panel
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let used = MainActor.assumeIsolated { self?.handle(event) ?? false }
            return used ? nil : event
        }
        panel.makeKeyAndOrderFront(nil)

        closeTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(Self.openSeconds))
            guard !Task.isCancelled else { return }
            self?.close()
        }
        // Closes when the screen locks or the Mac sleeps, so the codes aren't there when you come back.
        let closeNow: @Sendable (Notification) -> Void = { [weak self] _ in
            MainActor.assumeIsolated { self?.close() }
        }
        let workspace = NSWorkspace.shared.notificationCenter
        lockObservers = [
            (DistributedNotificationCenter.default(), DistributedNotificationCenter.default().addObserver(
                forName: Notification.Name("com.apple.screenIsLocked"), object: nil, queue: .main, using: closeNow
            )),
        ] + [NSWorkspace.screensDidSleepNotification, NSWorkspace.willSleepNotification, NSWorkspace.sessionDidResignActiveNotification].map {
            (workspace, workspace.addObserver(forName: $0, object: nil, queue: .main, using: closeNow))
        }
        NSAccessibility.post(
            element: NSApp as Any,
            notification: .announcementRequested,
            userInfo: [.announcement: model.announcement, .priority: NSAccessibilityPriorityLevel.high.rawValue]
        )
    }

    func close() {
        closeTask?.cancel()
        closeTask = nil
        for observer in lockObservers {
            observer.center.removeObserver(observer.token)
        }
        lockObservers = []
        if let monitor {
            NSEvent.removeMonitor(monitor)
            self.monitor = nil
        }
        model.codes = []
        guard let panel else { return }
        self.panel = nil
        panel.delegate = nil
        panel.orderOut(nil)
    }

    /// Clicking anywhere else closes the list, like Spotlight.
    func windowDidResignKey(_ notification: Notification) {
        close()
    }

    private func handle(_ event: NSEvent) -> Bool {
        guard let panel, event.window === panel else { return false }
        switch model.press(keyCode: Int(event.keyCode)) {
        case .copy(let code): onCopy(code)
        case .close: close()
        case .moved, .ignored: break
        }
        return true
    }
}

@MainActor
@Observable
final class RecentCodesModel {
    enum Action: Equatable {
        case copy(FoundCode)
        case close
        case moved
        case ignored
    }

    var codes: [FoundCode] = []
    var selection = 0
    /// Follows "Show the code in the notice", so a hidden code stays hidden here too.
    var showsCodes = true
    var lookbackMinutes = 10

    func press(keyCode: Int) -> Action {
        switch keyCode {
        case kVK_UpArrow:
            selection = max(selection - 1, 0)
            return .moved
        case kVK_DownArrow:
            selection = min(selection + 1, max(codes.count - 1, 0))
            return .moved
        case kVK_Return, kVK_ANSI_KeypadEnter:
            return codes.indices.contains(selection) ? .copy(codes[selection]) : .ignored
        case kVK_Escape:
            return .close
        default:
            return .ignored
        }
    }

    var announcement: String {
        let count = codes.count == 1 ? "1 recent code" : "\(codes.count) recent codes"
        return "\(count). Use the arrow keys to choose one, and Return to copy it."
    }
}

struct RecentCodesView: View {
    let model: RecentCodesModel
    let copy: (FoundCode) -> Void

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: "clock.arrow.circlepath")
                    .font(.title2)
                    .foregroundStyle(.secondary)
                    .accessibilityHidden(true)
                Text("Recent codes")
                    .font(.title2)
                Spacer()
                Text("Last \(model.lookbackMinutes) minutes")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 14)

            Divider()

            TimelineView(.periodic(from: .now, by: 15)) { timeline in
                VStack(spacing: 2) {
                    ForEach(Array(model.codes.enumerated()), id: \.offset) { index, code in
                        RecentCodeRow(code: code, showsCode: model.showsCodes, now: timeline.date, isSelected: index == model.selection)
                            .contentShape(Rectangle())
                            .onTapGesture { copy(code) }
                    }
                }
                .padding(6)
            }

            Divider()
            Text("↑↓ to choose · Return to copy · Esc to close")
                .font(.caption)
                .foregroundStyle(.secondary)
                .padding(.vertical, 8)
                .accessibilityHidden(true)
        }
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
    }
}

private struct RecentCodeRow: View {
    let code: FoundCode
    let showsCode: Bool
    let now: Date
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: code.source.symbol)
                .font(.system(size: 18, weight: .semibold))
                .foregroundStyle(code.source.tint)
                .frame(width: 24)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(code.sender)
                    .font(.headline)
                    .lineLimit(1)
                Text("\(code.source.name) · \(Confirmation.age(of: code.received, now: now))")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer(minLength: 8)
            if showsCode {
                Text(Self.readable(code.code))
                    .font(.system(size: 22, weight: .semibold, design: .monospaced))
                    .lineLimit(1)
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 54)
        .background(isSelected ? Color.accentColor.opacity(0.18) : .clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(accessibilityText)
        .accessibilityAddTraits(isSelected ? [.isSelected, .isButton] : .isButton)
    }

    private var accessibilityText: String {
        var parts = [code.sender, code.source.name, Confirmation.age(of: code.received, now: now)]
        if showsCode { parts.append("code \(code.code.map(String.init).joined(separator: " "))") }
        return parts.joined(separator: ", ")
    }

    /// "482 913", which is easier to read at a glance. Codes with letters or dashes stay as sent,
    /// and the copied code never has the space.
    static func readable(_ code: String) -> String {
        guard code.count == 6 || code.count == 8, code.allSatisfy(\.isASCIIDigit) else { return code }
        let middle = code.index(code.startIndex, offsetBy: code.count / 2)
        return code[..<middle] + " " + code[middle...]
    }
}

private extension Character {
    var isASCIIDigit: Bool { isASCII && isNumber }
}
