// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "Copy2FA",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "Copy2FA", targets: ["Copy2FA"]),
    ],
    targets: [
        // Pure logic with no access to Mail, the clipboard or the screen, so it can be tested on its own.
        .target(name: "Copy2FACore"),
        .executableTarget(name: "Copy2FA", dependencies: ["Copy2FACore"]),
        .testTarget(name: "Copy2FACoreTests", dependencies: ["Copy2FACore"]),
    ]
)
