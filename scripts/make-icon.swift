// Draws the app icon and writes it to Resources/AppIcon.icns.
//
//   swift scripts/make-icon.swift
import AppKit

let output = CommandLine.arguments.dropFirst().first ?? "Resources/AppIcon.icns"

/// Draws the icon on a 1024-point canvas, scaled to `pixels`.
func icon(pixels: Int) -> NSBitmapImageRep {
    let image = NSBitmapImageRep(
        bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8, samplesPerPixel: 4,
        hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
    )!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: image)
    let scale = NSAffineTransform()
    scale.scale(by: CGFloat(pixels) / 1024)
    scale.concat()

    let indigo = NSColor(srgbRed: 0.18, green: 0.24, blue: 0.78, alpha: 1)

    // The rounded tile, with the soft shadow macOS icons sit on.
    let tile = NSBezierPath(roundedRect: NSRect(x: 100, y: 100, width: 824, height: 824), xRadius: 185, yRadius: 185)
    NSGraphicsContext.saveGraphicsState()
    let tileShadow = NSShadow()
    tileShadow.shadowColor = .black.withAlphaComponent(0.3)
    tileShadow.shadowBlurRadius = 24
    tileShadow.shadowOffset = NSSize(width: 0, height: -12)
    tileShadow.set()
    indigo.setFill()
    tile.fill()
    NSGraphicsContext.restoreGraphicsState()
    NSGradient(
        starting: NSColor(srgbRed: 0.36, green: 0.56, blue: 1.0, alpha: 1),
        ending: indigo
    )!.draw(in: tile, angle: -90)

    // A code field holding six hidden digits.
    let field = NSBezierPath(roundedRect: NSRect(x: 212, y: 412, width: 600, height: 200), xRadius: 100, yRadius: 100)
    NSGraphicsContext.saveGraphicsState()
    let fieldShadow = NSShadow()
    fieldShadow.shadowColor = .black.withAlphaComponent(0.25)
    fieldShadow.shadowBlurRadius = 18
    fieldShadow.shadowOffset = NSSize(width: 0, height: -8)
    fieldShadow.set()
    NSColor.white.setFill()
    field.fill()
    NSGraphicsContext.restoreGraphicsState()

    indigo.setFill()
    for index in 0..<6 {
        let center = NSPoint(x: 310 + CGFloat(index) * 80.8, y: 512)
        NSBezierPath(ovalIn: NSRect(x: center.x - 28, y: center.y - 28, width: 56, height: 56)).fill()
    }

    NSGraphicsContext.restoreGraphicsState()
    return image
}

let iconset = FileManager.default.temporaryDirectory.appendingPathComponent("AppIcon.iconset")
try? FileManager.default.removeItem(at: iconset)
try FileManager.default.createDirectory(at: iconset, withIntermediateDirectories: true)

for points in [16, 32, 128, 256, 512] {
    for scale in [1, 2] {
        let name = scale == 1 ? "icon_\(points)x\(points).png" : "icon_\(points)x\(points)@2x.png"
        let png = icon(pixels: points * scale).representation(using: .png, properties: [:])!
        try png.write(to: iconset.appendingPathComponent(name))
    }
}

let iconutil = Process()
iconutil.executableURL = URL(fileURLWithPath: "/usr/bin/iconutil")
iconutil.arguments = ["--convert", "icns", iconset.path, "--output", output]
try iconutil.run()
iconutil.waitUntilExit()
guard iconutil.terminationStatus == 0 else { fatalError("iconutil failed") }
print("Wrote \(output)")
