using System.Collections.Generic;
using Godot;

namespace Worklings.Core.Stage;

/// The per-character half of a landed blow: what the world does when a
/// particular character connects.
///
/// Sits alongside ImpactFrames, not inside it, and the division is the point.
/// ImpactFrames owns the *weight* of a hit — hit-stop, camera shake, knockback,
/// the white flash — and is identical for everyone, because a punch that lands
/// differently per attacker reads as inconsistent physics rather than as
/// character. This owns the *identity*: the lightning, the shockwave, the
/// volley. One of those two systems should be tuned when combat feels weak, and
/// the other when it feels samey, and keeping them apart is what makes that a
/// decidable question.
///
/// **Everything hangs off the same clock the lunge uses.** AttackLunge already
/// derives a contact time from the attack animation's own length and
/// ActorAnimations.AttackImpactPoint, so there is exactly one authoritative
/// answer to "when does the blow land" and every cue here is scheduled relative
/// to it. That is why a telegraph can be guaranteed to finish on the strike
/// frame and a bolt can be made to land one frame *before* the body arrives:
/// both are arithmetic on a number that already exists, not a second timeline
/// to keep in sync with the first.
///
/// The clock is monotonic and shared across attacks rather than reset per swing,
/// so a scorch mark from the last exchange keeps burning while the next one is
/// thrown. Resetting it per attack was the first shape and it deleted every
/// mark the moment the fight moved on, which is the opposite of what a
/// persistent mark is for.
public sealed class AbilityVfx
{
    private sealed class Scheduled
    {
        public double At;
        public System.Func<VfxEffect>? Make;
        public VfxEffect? Live;
    }

    private readonly Node3D _world;
    private readonly Camera3D _camera;
    private readonly List<Scheduled> _cues = new();
    private double _clock;

    /// Off, everything here is skipped and combat falls back to impact frames
    /// alone. Kept as a switch for the same reason AttackersTravel is: the flat
    /// comparison is the only way to tell whether a layer is earning its place.
    public bool Enabled { get; set; } = true;

    public AbilityVfx(Node3D world, Camera3D camera)
    {
        _world = world;
        _camera = camera;
    }

    /// Schedule a character's signature against a target.
    ///
    /// `contactDelay` is the same number handed to AttackLunge.Begin — seconds
    /// from now until the blow lands. `big` is the crit-or-signature flag, and
    /// scales the effect rather than changing it: a crit should be the same
    /// character hitting harder, not a different character.
    public void Begin(AbilitySignature signature, StageActor attacker, StageActor victim,
                      double contactDelay, Color energy, bool big)
    {
        if (!Enabled || signature == AbilitySignature.None) return;

        // Captured now rather than read at fire time. The victim is being
        // knocked backwards by ImpactFrames on the very frame most of these
        // land, and an effect that tracks the knockback slides off the point of
        // impact — the mark belongs to where the blow landed, not to where the
        // body ended up.
        var victimFloor = Flatten(victim.Root.GlobalPosition);
        var victimChest = Chest(victim);
        var attackerChest = Chest(attacker);
        float scale = big ? 1.35f : 1f;

        switch (signature)
        {
            case AbilitySignature.LightningStrike:
                Lightning(contactDelay, victimFloor, victimChest, energy, scale);
                break;
            case AbilitySignature.FireShockwave:
                Fire(contactDelay, victimFloor, energy, scale);
                break;
            case AbilitySignature.GhostVolley:
                Ghosts(contactDelay, attackerChest, victimChest, victimFloor, energy, scale);
                break;
            case AbilitySignature.Roots:
                Roots(contactDelay, victimFloor, energy, scale);
                break;
        }
    }

    /// Zeus calling it down.
    ///
    /// The bolt is scheduled **one frame before contact**, not on it. That is
    /// deliberate and it is the single most important number in this file: it
    /// makes the impact read as *caused by the lightning* rather than as a
    /// collision that happened to be accompanied by some. Land them on the same
    /// frame and the eye picks the body as the cause, because the body has been
    /// travelling toward the target for the whole wind-up and the bolt has not.
    private void Lightning(double contact, Vector3 floor, Vector3 chest, Color energy, float scale)
    {
        // The telegraph: a wide dim glow on the victim's floor contracting onto
        // its feet across the entire wind-up. Uses the mark's own punch-in
        // curve run slowly — a growing scale is a strike, a shrinking one is a
        // warning, and they are the same three lines of code.
        Cue(0, () => new GroundMark(_world, floor, energy, 4.2f * scale, peak: 0.9f,
                                    fadeIn: contact * 0.85, hold: 0.05, fadeOut: 0.12,
                                    additive: true));

        const double OneFrame = 0.033;   // two at 60fps: one is below the eye's floor
        Cue(contact - OneFrame, () => new Bolt(_world, _camera, floor, energy, scale));
        Cue(contact - OneFrame, () => new LightPop(_world, chest,
                                                   VfxMaterials.Hot(energy, 0.55f),
                                                   peak: 16f * scale, range: 20f, life: 0.30));

        // The ground burst at the foot of the bolt. Small and quick — this is
        // the splash, not the event; the reference shot's base flare is maybe a
        // body-width across.
        Cue(contact, () => new Shockwave(_world, floor, energy, maxRadius: 4.2f * scale,
                                         life: 0.34, tongues: 14));
        // The burn. Violet-black rather than neutral: a lightning scorch that
        // matches the family colour keeps the read even after the light is gone.
        Cue(contact, () => new GroundMark(_world, floor, new Color(0.10f, 0.07f, 0.16f),
                                          3.4f * scale, peak: 0.9f,
                                          fadeIn: 0.06, hold: 2.2, fadeOut: 1.8));
    }

    /// The slam. Epicentre outward, fire at the rim, dark in the middle.
    ///
    /// Two rings rather than one: a fast tight one and a slower wide one. A
    /// single ring reads as a drawn circle expanding, where two at different
    /// speeds read as a front with depth behind it — cheapest possible
    /// approximation of the reference shot's boiling interior.
    private void Fire(double contact, Vector3 floor, Color energy, float scale)
    {
        Cue(0, () => new GroundMark(_world, floor, energy, 4.6f * scale, peak: 0.7f,
                                    fadeIn: contact * 0.8, hold: 0.05, fadeOut: 0.1,
                                    additive: true));

        // The scorch goes down *with* the wave, not after it, so the wave is
        // uncovering burnt ground rather than passing over clean ground and
        // leaving a stain behind as an afterthought.
        Cue(contact, () => new GroundMark(_world, floor, new Color(0.06f, 0.04f, 0.03f),
                                          6.5f * scale, peak: 0.92f,
                                          fadeIn: 0.12, hold: 2.6, fadeOut: 2.0));
        Cue(contact, () => new LightPop(_world, floor + new Vector3(0, 1.2f, 0),
                                        VfxMaterials.Hot(energy, 0.3f),
                                        peak: 14f * scale, range: 22f, life: 0.45));
        Cue(contact, () => new Shockwave(_world, floor, energy, maxRadius: 4.5f * scale,
                                         life: 0.30, tongues: 12));
        Cue(contact + 0.05, () => new Shockwave(_world, floor, energy,
                                                maxRadius: 9.5f * scale, life: 0.62, tongues: 20));
    }

    /// Paws thrown ahead of the cat.
    ///
    /// Timed so the **first** shot lands on the contact frame and the rest
    /// arrive behind it. The alternative — centring the volley on contact — puts
    /// half the flight after the strike and reads as the target being hit and
    /// then shot at.
    private void Ghosts(double contact, Vector3 from, Vector3 to, Vector3 floor, Color energy,
                        float scale)
    {
        const double Flight = 0.22;
        Cue(contact - Flight, () => new Volley(_world, from, to, energy, count: 4,
                                               stagger: 0.06, flight: Flight, size: scale));
        Cue(contact, () => new LightPop(_world, to, VfxMaterials.Hot(energy, 0.4f),
                                        peak: 9f * scale, range: 13f, life: 0.26));

        // Three scars, splayed and landing on consecutive frames rather than
        // together — a claw leaves parallel marks, and marks that appear
        // simultaneously read as one stamped decal.
        for (int i = 0; i < 3; i++)
        {
            float yaw = Mathf.DegToRad(-22f + i * 22f);
            double at = contact + i * 0.045;
            Cue(at, () => new GroundMark(_world, floor, energy, 2.4f * scale, peak: 0.55f,
                                         fadeIn: 0.05, hold: 1.4, fadeOut: 1.2,
                                         additive: true, yaw: yaw, aspect: 0.22f));
        }
    }

    /// The floor opening under the victim.
    ///
    /// The only signature that spends most of its budget *before* contact, which
    /// is what the Snag needs: its whip cracks at 0.50 of a clip that is barely
    /// a second long, so there is no room to tell the story afterwards. The
    /// wind-up is the tell and the crack is the punctuation.
    private void Roots(double contact, Vector3 floor, Color energy, float scale)
    {
        Cue(0, () => new GroundMark(_world, floor, energy, 3.4f * scale, peak: 0.9f,
                                    fadeIn: contact * 0.9, hold: 0.1, fadeOut: 0.5,
                                    additive: true));

        // Roots break the surface in a ragged ring through the wind-up, each on
        // its own beat, so the ground looks like it is being pushed through
        // rather than switched on.
        for (int i = 0; i < 5; i++)
        {
            float angle = Mathf.Tau * i / 5f + 0.4f;
            var at = floor + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.5f * scale;
            double when = contact * (0.35 + 0.11 * i);
            Cue(when, () => new GroundMark(_world, at, energy, 1.3f * scale, peak: 0.7f,
                                           fadeIn: 0.10, hold: 1.0, fadeOut: 0.9,
                                           additive: true, yaw: angle, aspect: 0.30f));
        }

        Cue(contact, () => new LightPop(_world, floor + new Vector3(0, 1f, 0), energy,
                                        peak: 8f * scale, range: 12f, life: 0.24));
        Cue(contact, () => new Shockwave(_world, floor, energy, maxRadius: 3.6f * scale,
                                         life: 0.40, tongues: 10));
        Cue(contact, () => new GroundMark(_world, floor, new Color(0.05f, 0.07f, 0.04f),
                                          3.0f * scale, peak: 0.75f,
                                          fadeIn: 0.10, hold: 2.0, fadeOut: 1.6));
    }

    private void Cue(double offset, System.Func<VfxEffect> make) =>
        _cues.Add(new Scheduled { At = _clock + System.Math.Max(0, offset), Make = make });

    /// Advance every scheduled and running effect.
    ///
    /// `scale` is the same hit-stop scale AttackLunge takes, and passing 0 here
    /// is what holds the bolt and the flash on the frozen frame instead of
    /// letting them play out behind a stopped fight.
    public void Tick(double delta, double scale = 1)
    {
        _clock += delta * scale;
        for (int i = _cues.Count - 1; i >= 0; i--)
        {
            var cue = _cues[i];
            if (_clock < cue.At) continue;
            cue.Live ??= cue.Make!();
            cue.Live.Advance(_clock - cue.At);
            if (cue.Live.Done) { cue.Live.Release(); _cues.RemoveAt(i); }
        }
    }

    /// Tear everything down — a cancelled attack, a scene reset, the end of a
    /// run. Effects that have not fired yet are dropped rather than fired early.
    public void Clear()
    {
        foreach (var cue in _cues) cue.Live?.Release();
        _cues.Clear();
    }

    /// Roughly where a body's mass is, for effects that should hit the
    /// character rather than the floor under it. Read off the mesh's own bounds
    /// because the roster spans a 2.8-scale cat and a 7.0-scale root snarl, and
    /// any fixed height is wrong for one of them.
    private static Vector3 Chest(StageActor actor)
    {
        if (actor.Mesh == null) return actor.Root.GlobalPosition + new Vector3(0, 1.2f, 0);
        var box = actor.Mesh.GlobalTransform * actor.Mesh.GetAabb();
        return box.GetCenter();
    }

    private static Vector3 Flatten(Vector3 v) => new(v.X, 0, v.Z);
}
