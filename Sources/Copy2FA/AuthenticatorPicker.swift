import AppKit
import Copy2FACore
import SwiftUI

/// A Spotlight-style search panel for picking an authenticator account.
///
/// The panel takes keyboard focus without activating Copy 2FA, so the app you
/// were in stays in front and ⌘V pastes there as soon as the code is copied.
@MainActor
final class AuthenticatorPicker: NSObject, NSWindowDelegate {
    var onCopy: (OTPAccount) -> Void = { _ in }
    var onClose: () -> Void = {}
    private var panel: KeyPanel?

    var isShown: Bool { panel != nil }

    func show(accounts: [OTPAccount]) {
        close()
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
        let content = NSHostingView(rootView: PickerView(
            accounts: accounts,
            // The app closes the picker once the code is ready, since a counter-based code is saved first.
            copy: { [weak self] account in self?.onCopy(account) },
            close: { [weak self] in self?.close() },
            resize: { [weak self] height in self?.resize(to: height) }
        ))
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
        panel.makeKeyAndOrderFront(nil)
    }

    func close() {
        guard let panel else { return }
        self.panel = nil
        panel.delegate = nil
        panel.orderOut(nil)
        onClose()
    }

    /// Keeps the top edge in place as the list grows or shrinks.
    private func resize(to height: CGFloat) {
        guard let panel, abs(panel.frame.height - height) > 0.5 else { return }
        var frame = panel.frame
        frame.origin.y += frame.height - height
        frame.size.height = height
        panel.setFrame(frame, display: true)
        panel.invalidateShadow()
    }

    /// Clicking anywhere else closes the picker, like Spotlight.
    func windowDidResignKey(_ notification: Notification) {
        close()
    }
}

/// A borderless panel that can still take keyboard focus.
private final class KeyPanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

private struct PickerView: View {
    let accounts: [OTPAccount]
    let copy: (OTPAccount) -> Void
    let close: () -> Void
    let resize: (CGFloat) -> Void

    @State private var query = ""
    @State private var selection = 0
    @FocusState private var searchIsFocused: Bool

    private static let rowHeight: CGFloat = 54
    private static let visibleRows = 6

    private var matches: [OTPAccount] {
        let words = query.split(separator: " ")
        guard !words.isEmpty else { return accounts }
        return accounts.filter { account in words.allSatisfy { account.displayName.localizedCaseInsensitiveContains($0) } }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: CodeSource.authenticator.symbol)
                    .font(.title2)
                    .foregroundStyle(CodeSource.authenticator.tint)
                    .accessibilityHidden(true)
                TextField("Search accounts", text: $query)
                    .textFieldStyle(.plain)
                    .font(.title2)
                    .focused($searchIsFocused)
                    .onSubmit(copySelection)
                    .onKeyPress(.upArrow) { move(by: -1) }
                    .onKeyPress(.downArrow) { move(by: 1) }
                    .onKeyPress(.escape) {
                        close()
                        return .handled
                    }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 14)

            Divider()
            list
        }
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
        .onAppear { searchIsFocused = true }
        .onChange(of: query) { selection = 0 }
        .onGeometryChange(for: CGFloat.self, of: { $0.size.height }, action: resize)
    }

    @ViewBuilder
    private var list: some View {
        if matches.isEmpty {
            Text(accounts.isEmpty ? "No accounts yet. Add them in Copy 2FA Settings." : "No matching accounts")
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity)
                .padding(18)
        } else {
            TimelineView(.periodic(from: .now, by: 1)) { timeline in
                ScrollViewReader { proxy in
                    ScrollView {
                        VStack(spacing: 2) {
                            ForEach(Array(matches.enumerated()), id: \.element.id) { index, account in
                                AccountRow(account: account, date: timeline.date, isSelected: index == selection)
                                    .id(account.id)
                                    .contentShape(Rectangle())
                                    .onTapGesture { copy(account) }
                            }
                        }
                        .padding(6)
                    }
                    .scrollIndicators(.never)
                    .frame(height: CGFloat(min(matches.count, Self.visibleRows)) * (Self.rowHeight + 2) + 10)
                    .onChange(of: selection) {
                        if matches.indices.contains(selection) { proxy.scrollTo(matches[selection].id) }
                    }
                }
            }
        }
    }

    private func move(by offset: Int) -> KeyPress.Result {
        guard !matches.isEmpty else { return .handled }
        selection = min(max(selection + offset, 0), matches.count - 1)
        return .handled
    }

    private func copySelection() {
        guard matches.indices.contains(selection) else { return }
        copy(matches[selection])
    }
}

private struct AccountRow: View {
    let account: OTPAccount
    let date: Date
    let isSelected: Bool

    var body: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text(account.issuer.isEmpty ? account.displayName : account.issuer)
                    .font(.headline)
                    .lineLimit(1)
                if !account.issuer.isEmpty && !account.name.isEmpty {
                    Text(account.name)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 8)
            if account.kind == .hotp {
                // Showing a counter-based code would use it up, so it's only made when copied.
                Text("Next code")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                Image(systemName: "arrow.right.circle")
                    .foregroundStyle(.secondary)
                    .frame(width: 18, height: 18)
                    .accessibilityHidden(true)
            } else {
                let remaining = account.secondsRemaining(at: date)
                Text(Self.grouped(account.code(at: date)))
                    .font(.system(size: 22, weight: .semibold, design: .monospaced))
                    .foregroundStyle(remaining <= 5 ? Color.orange : Color.primary)
                Countdown(fraction: Double(remaining) / Double(account.period))
                    .frame(width: 18, height: 18)
                    .accessibilityLabel("\(remaining) seconds left")
            }
        }
        .padding(.horizontal, 12)
        .frame(height: 54)
        .background(isSelected ? Color.accentColor.opacity(0.18) : .clear, in: RoundedRectangle(cornerRadius: 10, style: .continuous))
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    /// "482 913", which is easier to read at a glance. The copied code has no space.
    static func grouped(_ code: String) -> String {
        guard code.count >= 6 else { return code }
        let middle = code.index(code.startIndex, offsetBy: code.count / 2)
        return code[..<middle] + " " + code[middle...]
    }
}

private struct Countdown: View {
    let fraction: Double

    var body: some View {
        ZStack {
            Circle().stroke(.quaternary, lineWidth: 2.5)
            Circle()
                .trim(from: 0, to: fraction)
                .stroke(fraction <= 5.0 / 30.0 ? Color.orange : CodeSource.authenticator.tint, style: StrokeStyle(lineWidth: 2.5, lineCap: .round))
                .rotationEffect(.degrees(-90))
        }
    }
}
