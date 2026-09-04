using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// The live Workling: the one copy of `PetState` the app is running on, the
/// activity context around it, and the rules for changing either.
///
/// Everything that alters the pet goes through here — a care action, an observed
/// activity event, a minute passing, a delve resolving — so there is exactly one
/// place that decides when to write the save and when the pet is allowed to say
/// something. Before this, the scene held the state and every path that touched
/// it remembered to persist on its own.
///
/// Ported from Sources/Worklings/PetSession.swift, which is app code rather than
/// `CompanionCore`, so this is a rebuild against the same decisions rather than
/// a line-by-line port. The parts worth keeping are kept: the emote throttle,
/// the availability gate, and the rule that a reaction the user asked for is
/// never throttled.
///
/// Deliberately free of Godot except for its logging and the save location, so
/// the sequencing — reduce, observe, throttle — can be probed.
public sealed class PetSession
{
    public PetState State { get; private set; }

    /// Short-lived, never persisted. What the app currently believes you are
    /// doing.
    public ActivityContext Context { get; private set; } = ActivityContext.Quiet;

    public PetBrain Brain { get; }
    public SaveLocation Save { get; }

    /// False once a write has failed. A session that cannot save keeps running
    /// on the state it has rather than stopping — see `Persist`.
    public bool Saves { get; private set; } = true;

    /// Raised whenever the pet changes, for whatever reason. The scene redraws
    /// and the character window refreshes off this rather than each caller
    /// remembering to.
    public event System.Action<PetState>? StateChanged;

    /// Something worth showing over the pet's head. Already past the throttle.
    public event System.Action<PetReaction>? Reacted;

    /// A new day was noticed. The caller owns writing the stamp down, because
    /// where it is written is a shell question, not a pet one.
    public event System.Action<System.DateTimeOffset>? Woke;

    /// When the pet last showed an ACTIVITY-driven reaction. A care action the
    /// user took is exempt: a tapped button always reacts.
    private System.DateTimeOffset? _lastActivityEmoteAt;

    private readonly PetStateFileStore _store;

    /// Loads the Workling and brings it up to date. Deliberately does NOT greet
    /// a new day — see `Greet`.
    public PetSession(
        System.DateTimeOffset now,
        PetSimulationRates? rates = null,
        SaveLocation? save = null)
    {
        Brain = new PetBrain(rates);
        Save = save ?? SaveLocation.Resolve();
        _store = new PetStateFileStore(Save.Path);

        PetState loaded;
        try
        {
            loaded = _store.Load() ?? PetState.NewPet(now: now);
        }
        catch (System.Exception error)
        {
            Godot.GD.PushWarning($"Could not read {Save.Path}: {error.Message}. "
                               + "Running from a new pet; this session will not save.");
            loaded = PetState.NewPet(now: now);
            Saves = false;
        }

        // Advanced before anything else looks at it, so the pet you come back to
        // is the pet time actually left you rather than the one you closed.
        State = Brain.Advance(loaded, now);
        Persist();
    }

    /// Says good morning, if it is one.
    ///
    /// Separate from the constructor on purpose, and this is not a style
    /// preference: `Woke` and `Reacted` are how the caller writes the stamp down
    /// and shows the greeting, and nothing can be subscribed to a session that
    /// does not exist yet. Doing it in the constructor meant the stamp was never
    /// written — so the pet greeted a new day on every launch, and took the XP
    /// for it every time.
    public void Greet(System.DateTimeOffset now, System.DateTimeOffset? lastDailyWakeAt) =>
        CheckDailyWake(lastDailyWakeAt, now);

    public PetCareStatus CareStatus => PetCareStatus.Make(State);

    public bool IsFocusSessionActive => Context.IsWorking;

    public PetActionAvailability WorkLogAvailability(System.DateTimeOffset now) =>
        Brain.WorkLogAvailability(State, now);

    /// A minute of wall clock, or a wake from sleep. Expires a stale context
    /// first, so a work block the app never saw end stops influencing decay.
    public void Advance(System.DateTimeOffset now, System.DateTimeOffset? lastDailyWakeAt)
    {
        CheckDailyWake(lastDailyWakeAt, now);

        var expired = Context.Expiring(now);
        if (!expired.Equals(Context))
        {
            Context = expired;
        }

        var next = Brain.Advance(State, now, Context);
        if (!next.Equals(State))
        {
            Commit(next);
        }
    }

    /// An observed activity event: something you did, that the pet noticed.
    ///
    /// The context is reduced FIRST and the pre-reduction copy handed to the
    /// brain, because `WorkEnded` needs the `WorkingSince` the reduction is
    /// about to clear.
    public void Receive(ActivityEvent evt, System.DateTimeOffset now)
    {
        var previous = Context;
        Context = Context.Reducing(evt);

        var response = Brain.Observe(evt, State, now, previous);
        if (!response.State.Equals(State))
        {
            Commit(response.State);
        }

        // XP and needs are already applied. Only the *reaction* is throttled, so
        // a batch of commits or an agent finishing turn after turn emotes once
        // rather than stuttering.
        if (response.Reaction is PetReaction reaction
            && EmoteThrottle.ShouldEmote(_lastActivityEmoteAt, now))
        {
            _lastActivityEmoteAt = now;
            Reacted?.Invoke(reaction);
        }
    }

    /// Refreshes an ongoing signal — "still away" — without repeating its
    /// one-time reaction, so a genuine multi-hour absence keeps registering as
    /// away instead of quietly expiring back to quiet.
    public void ExtendActivity(ActivityEventKind kind, System.DateTimeOffset now) =>
        Context = Context.Reducing(SystemActivitySource.Event(kind, now));

    /// A care action the user took. Refused when the pet does not need it — you
    /// cannot feed a full Workling — which is why the menu greys those out.
    ///
    /// Saves immediately rather than on a timer: a desktop pet has no natural
    /// moment to close, so anything not written now is written never.
    public PetReaction? Perform(PetAction action, System.DateTimeOffset now)
    {
        if (!CareStatus.Availability(KindOf(action), State).IsEnabled)
        {
            return null;
        }

        var result = Brain.Perform(action, State, now);
        Commit(result.State);
        // Never throttled. A button the user pressed always answers.
        Reacted?.Invoke(result.Reaction);
        return result.Reaction;
    }

    /// Logs work by hand. The least verifiable event there is, which is why it
    /// runs through the cooldown and the daily cap before it is emitted at all.
    public bool LogWork(System.DateTimeOffset now)
    {
        if (!WorkLogAvailability(now).IsEnabled)
        {
            return false;
        }
        Receive(ManualActivitySource.Event(ActivityEventKind.WorkLogged, now), now);
        return true;
    }

    /// Starts or ends a focus session by hand, for when nothing is watching.
    public void ToggleFocusSession(System.DateTimeOffset now) =>
        Receive(ManualActivitySource.Event(
            IsFocusSessionActive ? ActivityEventKind.WorkEnded : ActivityEventKind.WorkStarted,
            now), now);

    /// A Workling changed somewhere that owns the change — gear equipped in the
    /// character window, a delve resolving. The session still owns the save.
    public void Replace(PetState state)
    {
        if (state.Equals(State))
        {
            return;
        }
        Commit(state);
    }

    private void Commit(PetState state)
    {
        State = state;
        Persist();
        StateChanged?.Invoke(State);
    }

    private void CheckDailyWake(System.DateTimeOffset? lastWakeAt, System.DateTimeOffset now)
    {
        if (!DailyWakeTracker.ShouldWake(lastWakeAt, now))
        {
            return;
        }
        // Announced before the event is delivered, so the caller has written the
        // stamp down by the time anything can go wrong with the delivery — a
        // crash mid-reaction must not leave the pet waking twice.
        Woke?.Invoke(now);
        Receive(SystemActivitySource.Event(ActivityEventKind.DailyWake, now), now);
    }

    private void Persist()
    {
        if (!Saves)
        {
            return;
        }
        try
        {
            _store.Save(State);
        }
        catch (System.Exception error)
        {
            // One failed write turns saving off rather than warning once a
            // minute for the rest of the session.
            Godot.GD.PushWarning($"Could not write {Save.Path}: {error.Message}");
            Saves = false;
        }
    }

    private static PetCareActionKind KindOf(PetAction action) => action.Kind switch
    {
        PetActionKind.Feed => PetCareActionKind.Feed,
        PetActionKind.Play => PetCareActionKind.Play,
        PetActionKind.Pet => PetCareActionKind.Pet,
        _ => PetCareActionKind.Sleep,
    };
}
