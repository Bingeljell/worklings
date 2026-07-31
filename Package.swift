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
            dependencies: ["CompanionCore"],
            resources: [
                .copy("../../assets/worklings-wildkin-spritesheet.png"),
                .copy("../../assets/worklings-elemental-spritesheet.png"),
                .copy("../../assets/worklings-relicborn-spritesheet.png"),
                .copy("../../assets/worklings-smoke-effects.png"),
                .copy("../../assets/foes/mote-idle.png"),
                .copy("../../assets/foes/mote-attack.png"),
                .copy("../../assets/foes/mote-hurt.png"),
                .copy("../../assets/dungeons/cache-warren-cave-backdrop.png"),
                .copy("../../assets/dungeons/cache-warren-atmosphere-overlay.png")
            ]
        ),
        .executableTarget(
            name: "CompanionCoreChecks",
            dependencies: ["CompanionCore"],
            path: "Tests/CompanionCoreChecks"
        )
    ]
)
