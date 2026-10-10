// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ScreenFerryKit",
    platforms: [.macOS(.v13)],
    products: [
        .library(name: "ScreenFerryKit", targets: ["ScreenFerryKit"]),
        .library(name: "ScreenFerryDDC", targets: ["ScreenFerryDDC"]),
        .library(name: "ScreenFerryDisplays", targets: ["ScreenFerryDisplays"]),
        .library(name: "ScreenFerryNet", targets: ["ScreenFerryNet"]),
        .library(name: "ScreenFerryAgent", targets: ["ScreenFerryAgent"]),
    ],
    dependencies: [
        .package(url: "https://github.com/apple/swift-certificates.git", from: "1.21.0"),
    ],
    targets: [
        .target(name: "ScreenFerryKit"),
        .target(name: "ScreenFerryDDC", dependencies: ["ScreenFerryKit"]),
        .target(name: "ScreenFerryDisplays", dependencies: ["ScreenFerryKit", "ScreenFerryDDC"]),
        .target(name: "ScreenFerryNet", dependencies: [
            "ScreenFerryKit",
            .product(name: "X509", package: "swift-certificates"),
        ]),
        .target(name: "ScreenFerryAgent", dependencies: ["ScreenFerryKit", "ScreenFerryNet", "ScreenFerryDisplays"]),
        .testTarget(name: "ScreenFerryKitTests", dependencies: ["ScreenFerryKit"]),
        .testTarget(name: "ScreenFerryNetTests", dependencies: ["ScreenFerryNet"]),
    ]
)
