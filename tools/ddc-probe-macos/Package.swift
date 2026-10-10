// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ddc-probe-macos",
    platforms: [.macOS(.v13)],
    dependencies: [
        .package(path: "../../apps/macos/ScreenFerryKit"),
    ],
    targets: [
        .executableTarget(
            name: "ddc-probe",
            dependencies: [
                .product(name: "ScreenFerryKit", package: "ScreenFerryKit"),
                .product(name: "ScreenFerryDDC", package: "ScreenFerryKit"),
            ]
        ),
    ]
)
