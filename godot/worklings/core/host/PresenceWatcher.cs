using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// Notices when you walk away, and when you come back.
///
/// Reads one number: how long it has been since the machine last saw any input,
/// system-wide. Never a keystroke, never a window title, never which app is
/// frontmost — and it needs no permission to ask, because the answer contains
/// nothing about what you were doing. That is the whole reason presence is
/// derived from idle time rather than from anything richer.
///
/// The decision — went idle, still idle, came back — is `PresenceEvaluator`,
/// which is ported and probed. This only supplies the number and delivers the
/// result.
///
/// Ported in behaviour from Sources/Worklings/PresenceMonitor.swift.
///
/// **macOS only.** The idle clock comes from CoreGraphics; Windows has
/// `GetLastInputInfo` and Linux has the X11/Wayland idle extensions, and neither
/// exists in this codebase in any language. On those platforms the watcher says
/// so once and stays inert, which leaves the pet unable to notice an absence
/// rather than wrong about one.
public sealed partial class PresenceWatcher : Node
{
    /// How long without input counts as away. Five minutes, matching Swift.
    [Export] public double IdleThreshold { get; set; } = PresenceEvaluator.DefaultIdleThreshold;

    /// How often to ask. Fifteen seconds, matching Swift — the threshold is
    /// minutes, so a finer poll would only cost battery.
    [Export] public double PollSeconds { get; set; } = 15;

    private readonly PetSession _session;

    /// Where the idle seconds come from. Injectable so the crossing can be
    /// driven deterministically without waiting five real minutes, and so the
    /// platform call sits behind one seam rather than being scattered.
    private readonly System.Func<double>? _idleSeconds;

    private bool _wasIdle;
    private double _timer;

    public PresenceWatcher(PetSession session, System.Func<double>? idleSeconds = null)
    {
        _session = session;
        _idleSeconds = idleSeconds ?? SystemIdleSeconds();
    }

    public bool IsAvailable => _idleSeconds is not null;

    /// The current idle reading, or null where there is no clock to read.
    /// Exposed so a check can confirm the platform call answers at all.
    public double? Sample() => _idleSeconds?.Invoke();

    public override void _Ready()
    {
        if (_idleSeconds is null)
        {
            GD.Print($"presence: unavailable on {OS.GetName()} — "
                   + "the pet will not notice you stepping away.");
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0) return;
        _timer = PollSeconds;
        Check(System.DateTimeOffset.Now);
    }

    /// One poll. Public so a test can step it without a timer.
    public void Check(System.DateTimeOffset now)
    {
        if (_idleSeconds is null) return;

        var signal = PresenceEvaluator.Signal(_idleSeconds(), _wasIdle, IdleThreshold);
        if (signal is not PresenceSignal decided) return;

        switch (decided)
        {
            case PresenceSignal.WentIdle:
                _wasIdle = true;
                _session.Receive(
                    SystemActivitySource.Event(ActivityEventKind.UserIdle, now), now);
                break;
            case PresenceSignal.StillIdle:
                // Refreshes the context WITHOUT repeating the reaction, so a
                // genuine multi-hour absence keeps registering as away instead
                // of quietly expiring back to quiet — and without resetting how
                // long the absence has been running.
                _session.ExtendActivity(ActivityEventKind.UserIdle, now);
                break;
            case PresenceSignal.Returned:
                _wasIdle = false;
                _session.Receive(
                    SystemActivitySource.Event(ActivityEventKind.UserReturned, now), now);
                break;
        }
    }

    /// The platform's idle clock, or null where there isn't one here yet.
    private static System.Func<double>? SystemIdleSeconds() =>
        OS.GetName() == "macOS" ? MacIdleSeconds : null;

    /// Seconds since the last input event of any kind, session-wide.
    ///
    /// `0xFFFFFFFF` is `kCGAnyInputEventType`, spelled as its raw value because
    /// neither Swift nor C# exposes the constant. `0` is
    /// `kCGEventSourceStateCombinedSessionState` — the whole login session
    /// rather than this process's private event stream, which would answer for
    /// input this app never receives and report the user as permanently away.
    private static double MacIdleSeconds() =>
        CGEventSourceSecondsSinceLastEventType(0, 0xFFFFFFFF);

    [System.Runtime.InteropServices.DllImport(
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern double CGEventSourceSecondsSinceLastEventType(
        int stateId, uint eventType);
}
