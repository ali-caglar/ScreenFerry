// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ScreenFerryKit",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "ScreenFerryKit", targets: ["ScreenFerryKit"]),
    ],
    targets: [
        .target(name: "ScreenFerryKit"),
        .testTarget(name: "ScreenFerryKitTests", dependencies: ["ScreenFerryKit"]),
    ]
)
