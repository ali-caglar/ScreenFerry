// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ScreenFerryKit",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "ScreenFerryKit", targets: ["ScreenFerryKit"]),
        .library(name: "ScreenFerryDDC", targets: ["ScreenFerryDDC"]),
    ],
    targets: [
        .target(name: "ScreenFerryKit"),
        .target(name: "ScreenFerryDDC", dependencies: ["ScreenFerryKit"]),
        .testTarget(name: "ScreenFerryKitTests", dependencies: ["ScreenFerryKit"]),
    ]
)
