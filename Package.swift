// swift-tools-version: 6.0

import PackageDescription

let package = Package(
    name: "BuildCompanion",
    platforms: [
        .macOS(.v14)
    ],
    products: [
        .library(name: "CompanionCore", targets: ["CompanionCore"]),
        .executable(name: "BuildCompanion", targets: ["BuildCompanion"]),
        .executable(name: "CompanionCoreChecks", targets: ["CompanionCoreChecks"])
    ],
    targets: [
        .target(name: "CompanionCore"),
        .executableTarget(
            name: "BuildCompanion",
            dependencies: ["CompanionCore"]
        ),
        .executableTarget(
            name: "CompanionCoreChecks",
            dependencies: ["CompanionCore"],
            path: "Tests/CompanionCoreChecks"
        )
    ]
)
