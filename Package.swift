// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CodeCaller",
    platforms: [.macOS(.v14)],
    targets: [
        // Pure logic with no access to Mail, the clipboard or the screen, so it can be tested on its own.
        .target(name: "CodeCallerCore"),
        .testTarget(name: "CodeCallerCoreTests", dependencies: ["CodeCallerCore"]),
    ]
)
