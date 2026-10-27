import AppKit

extension NSVisualEffectView {
    /// The frosted, rounded background the app's floating panels share.
    static func panelBackground(cornerRadius: CGFloat) -> NSVisualEffectView {
        let view = NSVisualEffectView()
        view.material = .popover
        view.blendingMode = .behindWindow
        view.state = .active
        view.maskImage = roundedMask(radius: cornerRadius)
        return view
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

extension NSScreen {
    /// The screen the pointer is on, where a floating panel should appear.
    static var withMouse: NSScreen? {
        screens.first { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) } ?? main
    }
}
