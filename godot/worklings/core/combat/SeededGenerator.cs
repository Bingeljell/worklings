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
    ///
    /// The algorithm is **Lemire's multiply-shift**, not the modulo-with-
    /// rejection that the name suggests. Swift computes the full 128-bit product
    /// of the word and the bound and returns its HIGH half — so the result is
    /// the word's *position* in the unit interval scaled to the bound, not its
    /// remainder. For seed 1 and bound 5 the two disagree immediately: the raw
    /// word mod 5 is 0, and Swift returns 2.
    ///
    /// Verified against reference values captured from the Swift implementation
    /// across 17 bounds (including 1, powers of two, and UInt64.MaxValue) and 8
    /// seeds. There is no power-of-two shortcut: bound 8 goes through the same
    /// multiply as every other bound.
    public ulong NextBelow(ulong upperBound)
    {
        if (upperBound <= 1) return 0;

        ulong random = Next();
        ulong high = System.Math.BigMul(random, upperBound, out ulong low);
        if (low < upperBound)
        {
            // The one range of words that would bias the result. Swift redraws
            // rather than correcting, so the stream position depends on the
            // values drawn — reproducing the loop is what keeps it in step.
            ulong t = (0UL - upperBound) % upperBound;
            while (low < t)
            {
                random = Next();
                high = System.Math.BigMul(random, upperBound, out low);
            }
        }
        return high;
    }

    /// An integer in [lower, upper], matching Swift's `Int.random(in:using:)`.
    public int NextInt(int lower, int upper)
    {
        if (upper <= lower) return lower;
        return lower + (int)NextBelow((ulong)(upper - lower + 1));
    }

    /// A double in a CLOSED range [lower, upper], matching Swift's
    /// `Double.random(in: a...b, using:)`.
    ///
    /// Deliberately separate from NextDouble, because Swift's two range
    /// overloads do not draw the same way — and the difference is the opposite
    /// of what you would guess:
    ///
    ///   half-open `0..<1`  ->  the LOW 53 bits   (word & (2^53 - 1))
    ///   closed    `a...b`  ->  the HIGH 53 bits  (word >> 11)
    ///
    /// Both consume exactly one word. Determined by deriving the mapping from
    /// reference values captured out of the Swift implementation, after two
    /// wrong guesses from reasoning about the stdlib source. Every strike draws
    /// its damage swing from a closed range, so getting this wrong would
    /// desynchronise every fight.
    public double NextDoubleClosed(double lower, double upper)
    {
        double unitRandom = (Next() >> 11) * (1.0 / 9007199254740992.0);
        return (upper - lower) * unitRandom + lower;
    }
}
