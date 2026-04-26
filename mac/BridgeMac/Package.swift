// swift-tools-version: 5.10
import PackageDescription

let package = Package(
    name: "BridgeMac",
    platforms: [
        .macOS(.v13)
    ],
    products: [
        .executable(name: "BridgeMac", targets: ["BridgeMacApp"])
    ],
    targets: [
        .executableTarget(
            name: "BridgeMacApp",
            path: "Sources/BridgeMacApp"
        )
    ]
)
