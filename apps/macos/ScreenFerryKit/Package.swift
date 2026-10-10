// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ScreenFerryKit",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "ScreenFerryKit", targets: ["ScreenFerryKit"]),
        .library(name: "ScreenFerryDDC", targets: ["ScreenFerryDDC"]),
        .library(name: "ScreenFerryDisplays", targets: ["ScreenFerryDisplays"]),
    ],
    targets: [
        .target(name: "ScreenFerryKit"),
        .target(name: "ScreenFerryDDC", dependencies: ["ScreenFerryKit"]),
        .target(name: "ScreenFerryDisplays"),
        .testTarget(name: "ScreenFerryKitTests", dependencies: ["ScreenFerryKit"]),
    ]
)
