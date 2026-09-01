namespace Worklings.Core.Combat;

/// A deterministic pseudo-random generator for combat resolution.
///
/// The rest of the core is deterministic — the same state and clock always
/// produce the same pet — and combat keeps that contract so a fight is
/// reproducible and testable. Seeding from the save state plus a per-delve
/// nonce makes every roll (hit, crit, damage variance) replayable exactly,
/// while still feeling varied turn to turn.
///
/// SplitMix64: tiny, fast, well-distributed. Ported from
/// Sources/CompanionCore/SeededGenerator.swift — the constants and the order of
/// operations must match exactly or a given seed stops producing the same
/// fight, which is the whole point of it.
///
/// A struct in Swift, a struct here: it is copied by value into encounter
/// snapshots and must not alias.
public struct SeededGenerator
{
    private ulong _state;

    public SeededGenerator(ulong seed) => _state = seed;

    /// Swift's `&+` and `&*` are wrapping operators. C# arithmetic on ulong
    /// wraps by default outside a `checked` context, so these match — but the
    /// file must never be compiled with CheckForOverflowUnderflow enabled.
    public ulong Next()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// A double in [0, 1), drawn exactly the way Swift's
    /// `Double.random(in: 0..<1, using:)` does it. Matching this is what keeps a
    /// seed producing the same fight in both languages, and it is not the
    /// obvious implementation.
    ///
    /// Swift calls `next(upperBound: 1 << 53)` and multiplies by `ulpOfOne / 2`
    /// (2^-53). For an upper bound of 2^53 the rejection loop inside
    /// `next(upperBound:)` never triggers, so it reduces to `next() % 2^53` —
    /// the LOW 53 bits, not the high ones. Taking the high bits (`>> 11`) gives
    /// a perfectly uniform double that diverges from Swift on every single
    /// draw. Caught by comparing against reference values captured from the
    /// Swift implementation, not by reasoning about it.
    public double NextDouble() => (Next() & ((1UL << 53) - 1)) * (1.0 / 9007199254740992.0);

    /// Returns true with the given probability, clamped to 0...1. The single
    /// entry point combat uses for a yes/no roll (does a Strike land, does it
    /// crit), so every such decision draws from the same stream in a defined
    /// order.
    public bool Chance(double probability)
    {
        double p = System.Math.Clamp(probability, 0.0, 1.0);
        if (p <= 0) return false;
        if (p >= 1) return true;
        return NextDouble() < p;
    }

    /// Swift's `RandomNumberGenerator.next(upperBound:)`, reproduced exactly —
    /// including the rejection loop, so a bounded draw consumes the same number
    /// of words from the stream as Swift does. That matters as much as the value
    /// itself: a stream that desynchronises makes every later roll diverge.
    public ulong NextBelow(ulong upperBound)
    {
        if (upperBound <= 1) return 0;
        ulong tmp = (ulong.MaxValue % upperBound) + 1;
        ulong range = tmp == upperBound ? 0 : tmp;
        ulong random;
        do { random = Next(); } while (random < range);
        return random % upperBound;
    }

    /// An integer in [lower, upper], matching Swift's `Int.random(in:using:)`.
    public int NextInt(int lower, int upper)
    {
        if (upper <= lower) return lower;
        return lower + (int)NextBelow((ulong)(upper - lower + 1));
    }
}
