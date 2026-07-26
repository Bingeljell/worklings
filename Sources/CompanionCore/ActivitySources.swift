import Foundation

/// Real activity events, tagged distinctly from `SimulatedActivitySource` so a
/// live session and a debug rehearsal are never confused in the event stream.
public enum SystemActivitySource {
    public static let sourceId = "system"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}

/// Events the user explicitly logs by hand — self-reported, and therefore
/// tagged distinctly from externally verifiable sources so fairness rules
/// can treat them differently later.
public enum ManualActivitySource {
    public static let sourceId = "manual"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}

/// Commits in a repository the user explicitly connected, surfaced as
/// `milestone` events. Tagged distinctly from `manual`/`simulated` so fairness
/// rules can later weigh a local commit differently from a self-reported log.
public enum GitActivitySource {
    public static let sourceId = "git"

    public static func event(_ kind: ActivityEventKind, at timestamp: Date) -> ActivityEvent {
        ActivityEvent(kind: kind, timestamp: timestamp, sourceId: sourceId)
    }
}

/// The pure decision behind the local-git source: given a change in a
/// repository's HEAD, how many `milestone` events does it represent?
///
/// Deliberately free of any git invocation so it is deterministically
/// checkable. The app target supplies the facts by shelling out to git; this
/// decides what the pet should see. It reasons only over commit identifiers and
/// their ancestry — never a message, diff, or path — so the source's structural
/// privacy is legible right here.
public enum GitCommitDelta {
    /// - Parameters:
    ///   - oldSHA: the previously observed HEAD, or nil if none is recorded yet
    ///     (a freshly connected repo). A nil baseline emits nothing, so
    ///     connecting a repo with history already behind it grants no XP.
    ///   - newSHA: the current HEAD.
    ///   - oldIsAncestorOfNew: whether `old` is an ancestor of `new`. False for
    ///     an amend, reset, or rebase that rewrote history instead of adding to
    ///     it — not forward progress, so it emits nothing.
    ///   - commitsAhead: how many *recently committed* commits `new` is ahead of
    ///     `old` — the watcher passes a recency-filtered count so a `pull` or
    ///     checkout that fast-forwards over old history earns nothing, only
    ///     commits actually made while watching do.
    ///   - maxPerCheck: an upper bound on the result, so a single HEAD movement
    ///     can never emit an unbounded burst of events (which would flood the
    ///     main actor). Defaults to a small cap.
    /// - Returns: how many `milestone` events to emit (never negative).
    ///
    /// Note: this answers "what does this HEAD movement represent," not "should
    /// we credit it now." The no-retro-credit-for-offline-commits rule is a
    /// caller-timing concern — the watcher syncs the baseline silently on
    /// connect and launch, and only acts on this result during live watching.
    public static func milestonesToEmit(
        oldSHA: String?,
        newSHA: String,
        oldIsAncestorOfNew: Bool,
        commitsAhead: Int,
        maxPerCheck: Int = 10
    ) -> Int {
        guard let oldSHA else { return 0 }
        guard newSHA != oldSHA else { return 0 }
        guard oldIsAncestorOfNew else { return 0 }
        return min(max(0, commitsAhead), max(0, maxPerCheck))
    }
}

/// Rate-limits the pet's *expressive* reaction so many events landing close
/// together — a batch of commits, an agent finishing turn after turn, several
/// sources firing at once — produce one emote, not a robotic stutter. Purely a
/// presentation concern: XP and needs still accrue per event upstream; only the
/// reaction is gated. Pure and deterministic so the window is checkable without
/// a real clock; the caller owns remembering `lastEmoteAt`.
public enum EmoteThrottle {
    public static let defaultMinimumInterval: TimeInterval = 5

    /// Whether a new reaction should be shown now, given when the pet last
    /// emoted. Always true if it has not emoted yet (or the interval is
    /// non-positive), so throttling never swallows the very first reaction.
    public static func shouldEmote(
        lastEmoteAt: Date?,
        now: Date,
        minimumInterval: TimeInterval = EmoteThrottle.defaultMinimumInterval
    ) -> Bool {
        guard minimumInterval > 0, let lastEmoteAt else {
            return true
        }
        return now.timeIntervalSince(lastEmoteAt) >= minimumInterval
    }
}

/// Decides whether the first interaction of a new calendar day has happened,
/// independent of how many times the app has launched that day. The caller
/// owns persisting `lastWakeAt`; this function only makes the determination.
public enum DailyWakeTracker {
    public static func shouldWake(
        lastWakeAt: Date?,
        now: Date,
        calendar: Calendar = .current
    ) -> Bool {
        guard let lastWakeAt else {
            return true
        }
        return !calendar.isDate(lastWakeAt, inSameDayAs: now)
    }
}

/// What a presence poll should do: fire a one-time transition event, keep an
/// ongoing absence alive without repeating its reaction, or nothing.
public enum PresenceSignal: Equatable, Sendable {
    case wentIdle
    case stillIdle
    case returned
}

/// Turns raw system idle seconds into a presence signal. Pure and
/// deterministic so the threshold crossing is testable without a real clock
/// or real input events; the caller owns polling and remembering `wasIdle`.
public enum PresenceEvaluator {
    public static let defaultIdleThreshold: TimeInterval = 5 * 60

    public static func signal(
        idleSeconds: TimeInterval,
        wasIdle: Bool,
        threshold: TimeInterval = PresenceEvaluator.defaultIdleThreshold
    ) -> PresenceSignal? {
        if idleSeconds >= threshold {
            return wasIdle ? .stillIdle : .wentIdle
        }
        return wasIdle ? .returned : nil
    }
}
