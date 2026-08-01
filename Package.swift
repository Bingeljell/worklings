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
                .copy("../../assets/dungeons/cache-warren-atmosphere-overlay.png"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/dungeon-bgm/dungeon-bgm__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-hit/combat-hit__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-slam/combat-slam__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-dodge/combat-dodge__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/countdown-tick/countdown-tick__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/victory-fanfare/victory-fanfare__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/defeat-sting/defeat-sting__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/foe-snare/foe-snare__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/foe-harden/foe-harden__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-crit/combat-crit__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-unleash/combat-unleash__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/combat-brace/combat-brace__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/foe-phase/foe-phase__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/foe-telegraph/foe-telegraph__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/foe-poof/foe-poof__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/encounter-enter/encounter-enter__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/boss-bgm/boss-bgm__v01.wav"),
                .copy("../../assets/audio/dungeon/worklings-dungeon/return-chime/return-chime__v01.wav")
            ]
        ),
        .executableTarget(
            name: "CompanionCoreChecks",
            dependencies: ["CompanionCore"],
            path: "Tests/CompanionCoreChecks"
        )
    ]
)
