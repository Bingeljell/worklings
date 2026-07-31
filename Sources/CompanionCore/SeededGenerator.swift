/// A deterministic pseudo-random generator for combat resolution.
///
/// The rest of `CompanionCore` is deterministic — the same state and clock
/// always produce the same pet — and combat has to keep that contract so a
/// fight is reproducible and testable. Seeding a fight from the save state plus
/// a per-delve nonce makes every roll (hit, crit, damage variance) replayable
/// exactly, while still feeling varied turn to turn.
///
/// The algorithm is SplitMix64: tiny, fast, and well-distributed. Conforming to
/// `RandomNumberGenerator` means the standard library's `random(in:using:)`
/// helpers work against it unchanged.
public struct SeededGenerator: RandomNumberGenerator, Equatable, Sendable {
    private var state: UInt64

    public init(seed: UInt64) {
        self.state = seed
    }

    public mutating func next() -> UInt64 {
        state = state &+ 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }

    /// Returns `true` with the given probability, clamped to `0...1`. The single
    /// entry point combat uses for a yes/no roll (does a Strike land, does it
    /// crit), so every such decision draws from the same stream in a defined
    /// order.
    public mutating func chance(_ probability: Double) -> Bool {
        let p = min(max(probability, 0), 1)
        if p <= 0 { return false }
        if p >= 1 { return true }
        return Double.random(in: 0..<1, using: &self) < p
    }
}
