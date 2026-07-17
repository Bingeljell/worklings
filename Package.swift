// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "Worklings",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .library(name: "CompanionCore", targets: ["CompanionCore"]),
        .executable(name: "Worklings", targets: ["Worklings"]),
        .executable(name: "CompanionCoreChecks", targets: ["CompanionCoreChecks"])
    ],
    targets: [
        .target(name: "CompanionCore"),
        .executableTarget(
            name: "Worklings",
            dependencies: ["CompanionCore"]
        ),
        .executableTarget(
            name: "CompanionCoreChecks",
            dependencies: ["CompanionCore"],
            path: "Tests/CompanionCoreChecks"
        )
    ]
)
