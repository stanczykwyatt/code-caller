// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "CodeCaller",
    platforms: [.macOS(.v14)],
    products: [
        .executable(name: "CodeCaller", targets: ["CodeCaller"]),
    ],
    targets: [
        // Pure logic with no access to Mail, the clipboard or the screen, so it can be tested on its own.
        .target(name: "CodeCallerCore"),
        .executableTarget(name: "CodeCaller", dependencies: ["CodeCallerCore"]),
        .testTarget(name: "CodeCallerCoreTests", dependencies: ["CodeCallerCore"]),
    ]
)
