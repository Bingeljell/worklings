#if DEBUG
import AppKit
import SceneKit
import simd
import SwiftUI

/// A dev tool, **not** wired into real gameplay: an orbitable view of the
/// shared `DungeonStageScene` room (`DungeonStage3D.swift`) with placeholder
/// party/foe billboards, so a dungeon's actual elevated-3/4 angle and framing
/// can be found by looking rather than guessed from a still image. Kept as a
/// permanent utility — every future dungeon needs its own angle checked the
/// same way, and it's how the Cache Warren's locked camera (now wired into
/// the real arena) was found in the first place.
@MainActor
enum DungeonStageCameraToolScene {
    /// 2× the original placeholder sizing — first pass at "too small," per
    /// the 2026-08-21 review. An eyeballed guess, not a calibrated scale.
    private static let testScale: CGFloat = 2.0

    /// The room's floor height from `DungeonStageScene.build()`
    /// (`DungeonStage3D.swift`) — a box centered at y=-0.15 with height 0.3,
    /// so its top surface is y=0. Ground truth, not eyeballed: billboards
    /// land on this exact surface regardless of which character is loaded.
    ///
    /// Was two values until 2026-09-01 (a raised foe platform at 0.4 and a
    /// party floor at 0.0), collapsed when the four-band blockout became a
    /// single flat floor. Both combatants now stand on the same surface.
    static let floorTopY: CGFloat = 0.0

    /// Where each character's own baked frame puts its ground-contact line,
    /// as a fraction of the frame's full height *below* the frame's
    /// vertical center. This is `CAM_TARGET`'s world-space z divided by the
    /// camera's ortho scale at bake time — not eyeballed, read off (Ram) or
    /// computed for (Flicker, Pangolin) each file's turntable rig during the
    /// 2026-08-26 `blender_stage_bake.py` pass. A billboard's node sits at
    /// `platformTopY + offset * planeHeight` so the character's actual feet
    /// land on the platform surface, whatever fraction of the frame they
    /// happen to occupy — a fixed node y regardless of which character is
    /// loaded was the bug: every character's ground line sits at a
    /// different height in its own frame (Ram 0.196, Flicker 0.335,
    /// Pangolin 0.179), so one fixed y only ever looked right for whichever
    /// character it was tuned against. Pangolin's value was recomputed
    /// 2026-08-27 after its ortho scale was widened 1.67→3.0 to stop its
    /// tail/silhouette clipping the frame edge (see bake-spec §10 Open) — a
    /// wider ortho scale shrinks the creature within the frame, which also
    /// moves its ground line closer to center, so this fraction isn't stable
    /// across an ortho-scale change and must be recomputed alongside it.
    private static let groundOffsetFraction: [String: CGFloat] = [
        "tempest-ram": 0.196,
        "forest-flicker": 0.335,
        "pangolin": 0.179,
    ]
    /// Used only for the mote-idle placeholder when no frames are found —
    /// roughly the middle of the observed range above.
    private static let defaultGroundOffsetFraction: CGFloat = 0.25

    /// Builds (or rebuilds) the two stage-corner billboards from a real
    /// rendered frame sequence — see `StageFrameLibrary`. Falls back to the
    /// mote-idle placeholder tinted for the party slot when no frames are
    /// found for the current picker selection, same as the single-still era.
    @discardableResult
    static func addDebugBillboards(
        to scene: SCNScene,
        foeSelection: StageFrameLibrary.Selection?,
        partySelection: StageFrameLibrary.Selection?
    ) -> (foe: SCNNode, party: SCNNode) {
        // Contact shadows first, so they render under the billboards — a
        // fixed ground decal at each corner's (x,z), not attached to the
        // billboard node itself: the billboard carries a
        // SCNBillboardConstraint that rotates it to face the camera, and a
        // child would inherit that rotation, tilting the shadow up off the
        // ground instead of lying flat. 2026-08-27, first of the "why do
        // these look pasted on" fixes (see dungeons.md).
        scene.rootNode.addChildNode(makeContactShadow(
            diameter: 2.2 * testScale * 0.4, at: SCNVector3(0, floorTopY + 0.006, -2)
        ))
        scene.rootNode.addChildNode(makeContactShadow(
            diameter: 2.8 * testScale * 0.4, at: SCNVector3(-3, floorTopY + 0.006, 8.5)
        ))

        let foePlane = SCNPlane(width: 2.2 * testScale, height: 2.2 * testScale)
        foePlane.firstMaterial?.isDoubleSided = true
        foePlane.firstMaterial?.lightingModel = .constant
        let foeNode = SCNNode(geometry: foePlane)
        foeNode.name = "stageFoeBillboard"
        foeNode.position = SCNVector3(0, floorTopY, -2)
        foeNode.constraints = [SCNBillboardConstraint()]
        scene.rootNode.addChildNode(foeNode)
        applyBillboard(foeSelection, to: foeNode, groundY: floorTopY, fallbackTint: nil)

        let partyPlane = SCNPlane(width: 2.8 * testScale, height: 2.8 * testScale)
        partyPlane.firstMaterial?.isDoubleSided = true
        partyPlane.firstMaterial?.lightingModel = .constant
        let partyNode = SCNNode(geometry: partyPlane)
        partyNode.name = "stagePartyBillboard"
        partyNode.position = SCNVector3(-3, floorTopY, 8.5)
        partyNode.constraints = [SCNBillboardConstraint()]
        scene.rootNode.addChildNode(partyNode)
        applyBillboard(partySelection, to: partyNode, groundY: floorTopY, fallbackTint: NSColor(calibratedRed: 0.6, green: 0.75, blue: 1.0, alpha: 1))

        return (foeNode, partyNode)
    }

    /// Swaps a single billboard's material to a new character/action/azimuth
    /// selection without touching the other billboard or the camera — used
    /// when a picker changes mid-session. Also repositions the node's y so
    /// the new character's feet land on `groundY` (see `groundOffsetFraction`
    /// above), since that offset differs per character.
    static func applyBillboard(_ selection: StageFrameLibrary.Selection?, to node: SCNNode, groundY: CGFloat, fallbackTint: NSColor?) {
        let plane = node.geometry as! SCNPlane
        let material = plane.firstMaterial!
        node.removeAction(forKey: "frameSequence")
        guard let selection, let images = StageFrameLibrary.shared.loadImages(for: selection), !images.isEmpty else {
            material.diffuse.contents = loadDungeonStageImage("mote-idle") ?? NSColor.systemPink
            material.multiply.contents = fallbackTint
            node.position.y = groundY + defaultGroundOffsetFraction * plane.height
            return
        }
        material.multiply.contents = nil
        material.diffuse.contents = images[0]
        let offset = groundOffsetFraction[selection.label] ?? defaultGroundOffsetFraction
        node.position.y = groundY + offset * plane.height
        guard images.count > 1 else { return }
        let fps = StageFrameLibrary.shared.playbackFPS(for: selection)
        node.runAction(StageFrameLibrary.frameSequenceAction(images: images, material: material, fps: fps), forKey: "frameSequence")
    }

    /// A starting guess at the elevated-3/4 angle — a baseline to drag from,
    /// not the answer.
    static func makeStartingCamera(in scene: SCNScene) -> SCNNode {
        DungeonStageScene.makeCamera(
            position: SCNVector3(0, 4.5, 11),
            lookingAt: SCNVector3(0, 0.5, -1),
            in: scene
        )
    }

    /// A flat, soft-edged dark oval lying on the ground under a billboard —
    /// without this, a character reads as a sticker floating above the
    /// floor rather than standing on it (2026-08-27 feedback: "they don't
    /// look like they're from the same universe... could be shadows").
    /// Not parented to the billboard — see the call site for why.
    private static func makeContactShadow(diameter: CGFloat, at position: SCNVector3) -> SCNNode {
        let plane = SCNPlane(width: diameter, height: diameter * 0.62)
        plane.firstMaterial?.diffuse.contents = contactShadowTexture
        plane.firstMaterial?.lightingModel = .constant
        plane.firstMaterial?.isDoubleSided = true
        plane.firstMaterial?.writesToDepthBuffer = false
        let node = SCNNode(geometry: plane)
        node.name = "contactShadow"
        node.eulerAngles = SCNVector3(-Double.pi / 2, 0, 0) // lie flat, normal up +Y
        node.position = position
        return node
    }

    /// A soft radial dark-to-transparent gradient, generated in code rather
    /// than shipped as an asset — same reasoning as the spark texture
    /// elsewhere in this file, there's nothing character-specific about a
    /// contact shadow's shape.
    private static let contactShadowTexture: NSImage = {
        let size = 128
        let image = NSImage(size: NSSize(width: size, height: size))
        image.lockFocus()
        if let ctx = NSGraphicsContext.current?.cgContext {
            // Pushed much darker/tighter than a typical soft AO falloff —
            // 2026-08-27: against the painted backdrop's already near-black
            // ground (sampled ~RGB 14-28), a gentle alpha gradient had
            // almost no headroom left to darken further and was invisible.
            // A near-opaque core with a fast falloff still reads as a real
            // shadow shape even with little room below the floor's own
            // value.
            let colors = [
                NSColor.black.withAlphaComponent(0.9).cgColor,
                NSColor.black.withAlphaComponent(0.5).cgColor,
                NSColor.black.withAlphaComponent(0.0).cgColor,
            ] as CFArray
            if let gradient = CGGradient(colorsSpace: CGColorSpaceCreateDeviceRGB(), colors: colors, locations: [0, 0.35, 1]) {
                ctx.drawRadialGradient(
                    gradient,
                    startCenter: CGPoint(x: size / 2, y: size / 2), startRadius: 0,
                    endCenter: CGPoint(x: size / 2, y: size / 2), endRadius: CGFloat(size / 2),
                    options: []
                )
            }
        }
        image.unlockFocus()
        return image
    }()

    private static let backdropNodeName = "flatBackdropPlane"
    private static let roomBandNames = ["bandFloor"]

    /// 2026-08-27 prototype: swaps the grey 3D blockout for a single flat
    /// painted-backdrop plane, so the two approaches can be judged side by
    /// side against the *real* locked camera before deciding between them —
    /// see dungeons.md's "no flat backdrop" call, which this is explicitly
    /// testing rather than assuming. Not wired into the real arena.
    static func setBackdropMode(_ enabled: Bool, in scene: SCNScene, cameraNode: SCNNode) {
        for name in roomBandNames {
            scene.rootNode.childNode(withName: name, recursively: false)?.isHidden = enabled
        }
        scene.rootNode.childNode(withName: backdropNodeName, recursively: true)?.removeFromParentNode()
        guard enabled, let image = loadDungeonStageImage("cache-warren-v1-noChars") else { return }

        // Placed along the camera's actual view axis (its world -Z, i.e.
        // `worldFront`), not a fixed world-space point — the locked camera
        // sits on a diagonal, so a fixed coordinate like (0, y, z) is off to
        // one side of where the camera is actually looking, which is
        // exactly what shifted the backdrop toward the top-right in the
        // first version. Distance chosen to clear the existing back wall
        // (z -4) and every character position; size derived from the
        // camera's own FOV so it exactly fills a 1280x720 (16:9) frame at
        // that depth — not eyeballed. Must clear the camera-to-target
        // distance (~30, i.e. roughly where the characters stand) or the
        // backdrop ends up nearer the camera than they are and blocks them
        // entirely — the first version used 22 and did exactly that.
        let distance: Double = 36
        let camPos = cameraNode.position
        let forward = cameraNode.worldFront
        let backdropPosition = SCNVector3(
            camPos.x + forward.x * CGFloat(distance),
            camPos.y + forward.y * CGFloat(distance),
            camPos.z + forward.z * CGFloat(distance)
        )
        let verticalFOVRadians = DungeonStageScene.cameraFieldOfView * .pi / 180
        let planeHeight = 2 * distance * tan(verticalFOVRadians / 2)
        let planeWidth = planeHeight * (1280.0 / 720.0)

        let plane = SCNPlane(width: CGFloat(planeWidth), height: CGFloat(planeHeight))
        plane.firstMaterial?.diffuse.contents = image
        plane.firstMaterial?.lightingModel = .constant
        plane.firstMaterial?.isDoubleSided = true

        let node = SCNNode(geometry: plane)
        node.name = backdropNodeName
        node.position = backdropPosition
        node.orientation = cameraNode.orientation
        scene.rootNode.addChildNode(node)
    }
}

/// Indexes the real Blender frame-sequence renders produced by
/// `image-to-3dlab/scripts/blender_stage_bake.py` for the 2026-08-26 rig pass
/// (Tempest Ram, Forest Flicker, Clockwork Pangolin) so the camera tool can
/// play real motion instead of a single still. Lives outside the repo, same
/// as the earlier single-frame `TempestRamElevationTest` it replaces — this
/// is dev-tool scaffolding, not shipped art. Falls back to the placeholder
/// billboard when the folder is missing (a fresh checkout, another machine).
///
/// Filenames are `<label>_<action>_az<az>_f<frame>.png`, one folder per
/// character × action × azimuth, written flat by the bake script. Frames are
/// pre-sorted at index time so playback never re-sorts on every material swap.
struct StageFrameLibrary {
    struct Selection: Hashable {
        var label: String
        var action: String
        var azimuth: Int
    }

    static let directory = "/Users/nikhilshahane/projects/worklings-blender-work/test-renders/stage-frames"

    /// label -> actions available for it, in the order first seen on disk.
    let actionsByLabel: [String: [String]]
    /// every (label, action) pair that has at least one azimuth baked.
    let labels: [String]

    /// Every character's `.blend` renders at this scene frame rate (confirmed
    /// for the Tempest Ram via the live RPC session; Forest Flicker and
    /// Pangolin were built the same way and not independently checked, so
    /// this is an assumption, not a per-file read).
    private static let nativeSceneFPS: Double = 24

    private let framePaths: [Selection: [(index: Int, path: String)]]

    static let shared = StageFrameLibrary(scanning: directory)

    init(scanning directory: String) {
        let fm = FileManager.default
        var byLabel: [String: [String]] = [:]
        var paths: [Selection: [(index: Int, path: String)]] = [:]
        var labelOrder: [String] = []

        let entries = (try? fm.contentsOfDirectory(atPath: directory)) ?? []
        // <label>_<action>_az<az>_f<frame>.png — label and action are
        // whatever text sits before/after the last "_az", so split from the
        // right rather than assuming either half is free of underscores.
        let pattern = try! NSRegularExpression(pattern: #"^(.+)_az(\d+)_f(\d+)\.png$"#)
        for entry in entries.sorted() {
            let full = entry as NSString
            guard let match = pattern.firstMatch(in: entry, range: NSRange(location: 0, length: full.length)) else { continue }
            let labelAction = full.substring(with: match.range(at: 1))
            guard let az = Int(full.substring(with: match.range(at: 2))) else { continue }
            guard let frameIndex = Int(full.substring(with: match.range(at: 3))) else { continue }
            guard let underscoreIndex = labelAction.firstIndex(of: "_") else { continue }
            let label = String(labelAction[labelAction.startIndex..<underscoreIndex])
            let action = String(labelAction[labelAction.index(after: underscoreIndex)...])

            if byLabel[label] == nil {
                byLabel[label] = []
                labelOrder.append(label)
            }
            if !byLabel[label]!.contains(action) {
                byLabel[label]!.append(action)
            }
            let selection = Selection(label: label, action: action, azimuth: az)
            paths[selection, default: []].append((index: frameIndex, path: "\(directory)/\(entry)"))
        }
        for key in paths.keys {
            paths[key]?.sort { $0.index < $1.index }
        }
        self.actionsByLabel = byLabel
        self.labels = labelOrder
        self.framePaths = paths
    }

    func loadImages(for selection: Selection) -> [NSImage]? {
        guard let frames = framePaths[selection], !frames.isEmpty else { return nil }
        return frames.compactMap { NSImage(contentsOfFile: $0.path) }
    }

    /// Reconstructs the real playback rate from the gap between consecutive
    /// baked source-frame indices, rather than assuming every sequence was
    /// subsampled the same way. `blender_stage_bake.py` renders every native
    /// frame (step 1) for snappy actions and every other frame (step 2) for
    /// the long idle loops — playing all of them back at one fixed rate
    /// silently turns a full-rate walk into 2x slow motion, which is exactly
    /// the bug this fixes.
    func playbackFPS(for selection: Selection) -> Double {
        guard let frames = framePaths[selection], frames.count > 1 else { return Self.nativeSceneFPS }
        let step = frames[1].index - frames[0].index
        guard step > 0 else { return Self.nativeSceneFPS }
        return Self.nativeSceneFPS / Double(step)
    }

    /// Drives the texture swap through `SCNAction.run` closures rather than a
    /// `CAKeyframeAnimation` on `diffuse.contents` — SceneKit's CA bridge
    /// (`CAKeyframeAnimationToC3DAnimation`) only understands animatable
    /// scalar/vector/color values, not an array of `NSImage`, and throws an
    /// NSException trying to prepare one (confirmed via the crash log: the
    /// throw happens inside `-[SCNMaterial addAnimationPlayer:forKey:]` the
    /// instant the scene's `pointOfView` is set). A plain action sequence
    /// sidesteps the CA bridge entirely.
    static func frameSequenceAction(images: [NSImage], material: SCNMaterial, fps: Double) -> SCNAction {
        let frameDuration = 1.0 / fps
        let steps = images.map { image in
            SCNAction.sequence([
                SCNAction.run { _ in material.diffuse.contents = image },
                SCNAction.wait(duration: frameDuration),
            ])
        }
        return SCNAction.repeatForever(SCNAction.sequence(steps))
    }
}

/// SceneKit's built-in `allowsCameraControl` manipulator turned out not to update
/// the `pointOfView` node's `position`/`eulerAngles` at all when driven through
/// SwiftUI's `SceneView` — the render visibly orbits, but the node reference this
/// file holds never changes, so there was nothing honest to read back. This
/// hand-rolled orbit (drag = azimuth/elevation, scroll = dolly) drives the same
/// node directly, so what gets read is guaranteed to be what moved the camera.
private struct OrbitCamera {
    // Defaults to the Cache Warren's locked shot (docs/design/dungeons.md),
    // not a generic guess — so opening the tool starts from the current
    // answer and any drag is a deviation from it, not from scratch.
    // Target re-centered 2026-08-27: the previous target (-3.60, -0.63, 5.16)
    // skewed the whole frame toward the top-right, leaving a large dead zone
    // bottom-left. Same azimuth/elevation/radius, only the look-at point moved.
    var target = SCNVector3(-1.92, -0.10, 2.29)
    var azimuthDegrees: Double = 59.7
    var elevationDegrees: Double = 39.7
    var radius: Double = 27.95
    /// Tilt around the camera's own line of sight — a "dutch angle." Not
    /// reachable by orbit/pan/dolly at all: those move *where* the camera is,
    /// this rotates the frame itself, e.g. so a diagonal that exits past the
    /// bottom edge instead points into the bottom-left corner.
    var rollDegrees: Double = 0

    /// Pure function of (target, azimuth, elevation, radius) — safe to read
    /// for display at any time, not just after `apply(to:)` has run.
    var computedPosition: SCNVector3 {
        let az = azimuthDegrees * .pi / 180
        let el = max(min(elevationDegrees, 85), -10) * .pi / 180
        let r = max(3, min(30, radius))
        return SCNVector3(
            target.x + CGFloat(r * cos(el) * sin(az)),
            target.y + CGFloat(r * sin(el)),
            target.z + CGFloat(r * cos(el) * cos(az))
        )
    }

    mutating func apply(to node: SCNNode) {
        elevationDegrees = max(min(elevationDegrees, 85), -10)
        radius = max(3, min(30, radius))
        rollDegrees = max(min(rollDegrees, 45), -45)
        node.position = computedPosition
        node.look(at: target)
        // look(at:) always levels the horizon, so roll has to be applied after,
        // as an extra rotation around the camera's own local forward axis.
        let roll = simd_quatf(angle: Float(rollDegrees * .pi / 180), axis: simd_float3(0, 0, 1))
        node.simdOrientation = node.simdOrientation * roll
    }

    /// Counter-rotates a raw screen-space drag delta by the current roll, so
    /// dragging still tracks what's visually on screen once the frame is
    /// tilted. Without this, orbit/pan kept reading dx/dy as if the horizon
    /// were level, so any nonzero roll turned every drag into a skewed,
    /// seemingly uncontrollable spin — the bug just reported.
    func unrolled(dx: Double, dy: Double) -> (Double, Double) {
        let r = -rollDegrees * .pi / 180
        return (dx * cos(r) - dy * sin(r), dx * sin(r) + dy * cos(r))
    }

    /// Slides the camera and what it's looking at sideways together — a true
    /// truck/pan, not a rotation — so framing (e.g. "the foe should sit in the
    /// top-right third, not dead center") can actually be composed. `dx`/`dy`
    /// are screen-space drag deltas; the horizontal shift is resolved against
    /// the current azimuth so "drag right" always reads as "content moves
    /// right," regardless of which way the camera is currently facing.
    mutating func pan(dx: Double, dy: Double) {
        let az = azimuthDegrees * .pi / 180
        let speed = 0.02 * (radius / 12.65)
        target.x -= CGFloat(cos(az) * dx * speed)
        target.z -= CGFloat(-sin(az) * dx * speed)
        target.y += CGFloat(dy * speed)
    }
}

/// Owns orbit-drag and scroll-to-dolly via raw AppKit events instead of
/// SwiftUI gestures. SwiftUI's `DragGesture` and `MagnificationGesture`,
/// stacked on the same view, fought each other and produced the "camera goes
/// all over" jumpiness; `MagnificationGesture` also only understands trackpad
/// pinch, never a mouse's scroll wheel. Sits on top of the `SceneView`, which
/// now just renders — this is the only thing handling input.
private struct OrbitInputCatcher: NSViewRepresentable {
    var onDrag: (CGFloat, CGFloat) -> Void
    var onPan: (CGFloat, CGFloat) -> Void
    var onScroll: (CGFloat) -> Void

    func makeNSView(context: Context) -> CatcherView {
        let view = CatcherView()
        view.onDrag = onDrag
        view.onPan = onPan
        view.onScroll = onScroll
        return view
    }

    func updateNSView(_ nsView: CatcherView, context: Context) {
        nsView.onDrag = onDrag
        nsView.onPan = onPan
        nsView.onScroll = onScroll
    }

    final class CatcherView: NSView {
        var onDrag: ((CGFloat, CGFloat) -> Void)?
        var onPan: ((CGFloat, CGFloat) -> Void)?
        var onScroll: ((CGFloat) -> Void)?
        private var lastPoint: NSPoint?
        private var lastPanPoint: NSPoint?

        override var acceptsFirstResponder: Bool { true }

        // A parameter field grabbing focus mid-drag (or right after one ends)
        // could leave lastPoint/lastPanPoint stale — the next unrelated mouse
        // event would then measure its delta against a far-away anchor from a
        // previous drag, producing one huge, wild-looking rotation. Clearing
        // both whenever this view loses focus closes that off.
        override func resignFirstResponder() -> Bool {
            lastPoint = nil
            lastPanPoint = nil
            return super.resignFirstResponder()
        }

        override func mouseDown(with event: NSEvent) {
            window?.makeFirstResponder(self)
            lastPoint = event.locationInWindow
        }

        override func mouseDragged(with event: NSEvent) {
            let p = event.locationInWindow
            if let last = lastPoint {
                // AppKit's window coordinates are bottom-left-origin; flip dy so
                // "drag down" reads as positive, matching SwiftUI's convention.
                let dx = p.x - last.x
                let dy = -(p.y - last.y)
                // Belt-and-braces against any stale anchor slipping through:
                // a single mouse-move event this large isn't a real drag.
                if abs(dx) < 250, abs(dy) < 250 {
                    onDrag?(dx, dy)
                }
            }
            lastPoint = p
        }

        override func mouseUp(with event: NSEvent) {
            lastPoint = nil
        }

        // Right-drag (or two-finger secondary-click drag on a trackpad) pans
        // instead of orbiting — composing where things sit in frame.
        override func rightMouseDown(with event: NSEvent) {
            window?.makeFirstResponder(self)
            lastPanPoint = event.locationInWindow
        }

        override func rightMouseDragged(with event: NSEvent) {
            let p = event.locationInWindow
            if let last = lastPanPoint {
                let dx = p.x - last.x
                let dy = -(p.y - last.y)
                if abs(dx) < 250, abs(dy) < 250 {
                    onPan?(dx, dy)
                }
            }
            lastPanPoint = p
        }

        override func rightMouseUp(with event: NSEvent) {
            lastPanPoint = nil
        }

        override func scrollWheel(with event: NSEvent) {
            onScroll?(event.scrollingDeltaY)
        }
    }
}

/// `TextField(value:format:)` commits its binding on every keystroke, not on
/// Return/blur — so typing over an old value without first clearing it edits
/// in place (e.g. "70" + typing "61.1" over it can land on something like
/// "661.1"), and each half-typed digit moves the camera immediately. Once a
/// stray intermediate number lands past the clamp range, it just pegs at the
/// boundary and further digits look like "it keeps moving ahead" instead of
/// settling back on the value you meant to re-enter. This edits a local
/// string and only writes through to `orbit` on Return or losing focus, so
/// mid-edit keystrokes never touch the camera.
private struct CommitField: View {
    let label: String
    @Binding var value: Double
    @State private var text: String = ""
    @FocusState private var isFocused: Bool

    var body: some View {
        HStack(spacing: 3) {
            Text(label).foregroundStyle(.secondary)
            TextField("", text: $text)
                .textFieldStyle(.roundedBorder)
                .frame(width: 56)
                .focused($isFocused)
                .onSubmit { commit() }
                .onChange(of: isFocused) { _, focused in
                    if !focused { commit() }
                }
                .onAppear { text = formatted(value) }
                .onChange(of: value) { _, newValue in
                    if !isFocused { text = formatted(newValue) }
                }
        }
    }

    private func commit() {
        if let parsed = Double(text) {
            value = parsed
        }
        text = formatted(value)
    }

    private func formatted(_ v: Double) -> String {
        String(format: "%.2f", v)
    }
}

struct DungeonStageCameraToolView: View {
    @State private var scene = DungeonStageScene.build()
    @State private var cameraNode: SCNNode
    @State private var orbit = OrbitCamera()
    @State private var foeNode: SCNNode
    @State private var partyNode: SCNNode

    private let library = StageFrameLibrary.shared
    // Forest Flicker is the mini-boss/foe of the pair, so it defaults to the
    // az-35 "foe" corner slot; Tempest Ram is a party Workling, so it
    // defaults to the az-245 "party" slot. Either is swappable via the
    // pickers below — this is just a reasonable starting point.
    @State private var foeLabel = "forest-flicker"
    @State private var foeAction = "ForestFlicker_Walk_Feline"
    @State private var partyLabel = "tempest-ram"
    // Idle, not the walk cycle — this is the one action currently baked
    // with the Ram's electrical-crackle material (2026-08-27), so it's the
    // default that actually shows the effect on launch rather than the
    // plain-material walk frames.
    @State private var partyAction = "RamIdle_Breathe_Paw"
    @State private var backdropMode = false
    @State private var lightingTintMode = false
    @State private var controlsCollapsed = false
    // Sampled 2026-08-27 from the actual backdrop PNG at roughly where each
    // corner's character stands (party near the burrow, foe near the
    // crystal path) — not guessed. Normalized so the brightest channel is
    // 1.0, so multiplying a sprite by this shifts its color balance toward
    // the backdrop's local light without crushing its overall brightness.
    private static let partyLightingTint = NSColor(calibratedRed: 1.00, green: 0.94, blue: 0.55, alpha: 1)
    private static let foeLightingTint = NSColor(calibratedRed: 0.96, green: 1.00, blue: 0.76, alpha: 1)

    // 2026-08-28: the flat multiply tint above shifts a sprite's *average*
    // color but can't put a bright side and a dark side on it — the actual
    // complaint (Ram lit flat next to a warm torch it's standing right next
    // to; Flicker lit flat in a cyan crystal glow) is a missing light
    // *direction*, not a missing color. This fakes one: a linear gradient
    // across the billboard's own UV space, warm/bright on the side facing
    // that corner's real light source and cooler/dimmer on the far side —
    // supersedes the tint when both are on (see `applyLighting`). Direction
    // is a guess at UV orientation (untested — V may run the opposite way
    // SceneKit's texcoord convention assumes) so it's exposed as live
    // typeable fields below rather than hardcoded, same reasoning as every
    // other empirically-tuned number in this file.
    @State private var gradientLightMode = false
    @State private var gradientStrength: Double = 0.6
    @State private var partyLightDirX: Double = -0.8
    @State private var partyLightDirY: Double = -0.6
    @State private var foeLightDirX: Double = 0.9
    @State private var foeLightDirY: Double = 0.2
    private static let partyGradientLit = NSColor(calibratedRed: 1.35, green: 1.15, blue: 0.85, alpha: 1)
    private static let partyGradientShadow = NSColor(calibratedRed: 0.72, green: 0.78, blue: 0.95, alpha: 1)
    private static let foeGradientLit = NSColor(calibratedRed: 0.85, green: 1.15, blue: 1.25, alpha: 1)
    private static let foeGradientShadow = NSColor(calibratedRed: 0.85, green: 0.80, blue: 0.75, alpha: 1)

    init() {
        let scene = DungeonStageScene.build()
        let library = StageFrameLibrary.shared
        let nodes = DungeonStageCameraToolScene.addDebugBillboards(
            to: scene,
            foeSelection: library.labels.contains("forest-flicker")
                ? StageFrameLibrary.Selection(label: "forest-flicker", action: "ForestFlicker_Walk_Feline", azimuth: 35) : nil,
            partySelection: library.labels.contains("tempest-ram")
                ? StageFrameLibrary.Selection(label: "tempest-ram", action: "RamIdle_Breathe_Paw", azimuth: 245) : nil
        )
        _scene = State(initialValue: scene)
        _cameraNode = State(initialValue: DungeonStageCameraToolScene.makeStartingCamera(in: scene))
        _foeNode = State(initialValue: nodes.foe)
        _partyNode = State(initialValue: nodes.party)
    }

    var body: some View {
        ZStack(alignment: .topLeading) {
            SceneView(scene: scene, pointOfView: cameraNode, options: [])
                .ignoresSafeArea()

            OrbitInputCatcher(
                onDrag: { dx, dy in
                    let (rdx, rdy) = orbit.unrolled(dx: Double(dx), dy: Double(dy))
                    orbit.azimuthDegrees -= rdx * 0.3
                    orbit.elevationDegrees += rdy * 0.3
                    orbit.apply(to: cameraNode)
                },
                onPan: { dx, dy in
                    let (rdx, rdy) = orbit.unrolled(dx: Double(dx), dy: Double(dy))
                    orbit.pan(dx: rdx, dy: rdy)
                    orbit.apply(to: cameraNode)
                },
                onScroll: { deltaY in
                    orbit.radius -= Double(deltaY) * 0.05
                    orbit.apply(to: cameraNode)
                }
            )
            .ignoresSafeArea()

            VStack(alignment: .leading, spacing: 8) {
                // 2026-08-28: the panel's translucent background was
                // overlapping the party billboard at this camera framing —
                // a fixed-position always-expanded overlay and a fixed
                // character position will collide for *some* combination of
                // params sooner or later, so collapsing it (rather than
                // relocating it, which just moves the same collision
                // elsewhere) is the durable fix. The toggle button itself
                // stays put as a small persistent tab so it's always
                // findable.
                Button(controlsCollapsed ? "▸ controls" : "▾ controls") { controlsCollapsed.toggle() }
                    .buttonStyle(.plain)
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundStyle(.white.opacity(0.85))
                    .keyboardShortcut("c", modifiers: [])
                if !controlsCollapsed {
                    Text("Drag to orbit · right-drag to pan · scroll to dolly")
                        .font(.system(.caption2, design: .monospaced))
                        .foregroundStyle(.white.opacity(0.7))
                    Text(liveSummary)
                        .font(.system(.caption, design: .monospaced))
                    parameterFields
                    actorPickers
                    Toggle("flat painted backdrop (prototype)", isOn: $backdropMode)
                        .toggleStyle(.checkbox)
                        .font(.system(.caption2, design: .monospaced))
                        .onChange(of: backdropMode) { _, enabled in
                            DungeonStageCameraToolScene.setBackdropMode(enabled, in: scene, cameraNode: cameraNode)
                        }
                    Toggle("sync lighting tint to backdrop (prototype)", isOn: $lightingTintMode)
                        .toggleStyle(.checkbox)
                        .font(.system(.caption2, design: .monospaced))
                        .onChange(of: lightingTintMode) { _, _ in updateLighting() }
                    Toggle("directional light gradient (prototype)", isOn: $gradientLightMode)
                        .toggleStyle(.checkbox)
                        .font(.system(.caption2, design: .monospaced))
                        .onChange(of: gradientLightMode) { _, _ in updateLighting() }
                    if gradientLightMode {
                        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 4) {
                            GridRow {
                                Text("grad strength").foregroundStyle(.secondary)
                                numberField("", Binding(get: { gradientStrength }, set: { gradientStrength = $0; updateLighting() }))
                            }
                            GridRow {
                                Text("party dir").foregroundStyle(.secondary)
                                numberField("x", Binding(get: { partyLightDirX }, set: { partyLightDirX = $0; updateLighting() }))
                                numberField("y", Binding(get: { partyLightDirY }, set: { partyLightDirY = $0; updateLighting() }))
                            }
                            GridRow {
                                Text("foe dir").foregroundStyle(.secondary)
                                numberField("x", Binding(get: { foeLightDirX }, set: { foeLightDirX = $0; updateLighting() }))
                                numberField("y", Binding(get: { foeLightDirY }, set: { foeLightDirY = $0; updateLighting() }))
                            }
                        }
                        .font(.system(.caption2, design: .monospaced))
                    }
                    HStack(spacing: 8) {
                        Button("Copy to Clipboard") { copyToClipboard() }
                            .keyboardShortcut(.space, modifiers: [])
                        Button("Reset") { reset() }
                            .keyboardShortcut("r", modifiers: [.command])
                    }
                }
            }
            .padding(10)
            .background(.black.opacity(controlsCollapsed ? 0.35 : 0.55), in: RoundedRectangle(cornerRadius: 8))
            .foregroundStyle(.white)
            .padding(12)
        }
        // Locked to 16:9 — the numbers you dial in here are only meaningful
        // against a fixed frame shape. A resizable window let the same
        // camera transform read as a different composition depending on
        // whatever size the window happened to be, which is very likely why
        // retyping saved numbers didn't reproduce what was found earlier.
        .frame(width: 1280, height: 720)
        .onAppear { orbit.apply(to: cameraNode) }
    }

    /// Every axis, typeable — precise where a drag is fiddly, and the fields
    /// live-update from mouse input too since both write through `orbit`.
    private var parameterFields: some View {
        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 4) {
            GridRow {
                Text("target").foregroundStyle(.secondary)
                numberField("x", targetBinding(\.x))
                numberField("y", targetBinding(\.y))
                numberField("z", targetBinding(\.z))
            }
            GridRow {
                Text("orbit").foregroundStyle(.secondary)
                numberField("az°", doubleBinding(\.azimuthDegrees))
                numberField("el°", doubleBinding(\.elevationDegrees))
                numberField("r", doubleBinding(\.radius))
            }
            GridRow {
                Text("roll").foregroundStyle(.secondary)
                numberField("°", doubleBinding(\.rollDegrees))
            }
        }
        .font(.system(.caption2, design: .monospaced))
    }

    private func numberField(_ label: String, _ value: Binding<Double>) -> some View {
        CommitField(label: label, value: value)
    }

    /// Character/action pickers for the two stage-corner billboards, so the
    /// three real animated rigs (2026-08-26: Tempest Ram, Forest Flicker,
    /// Clockwork Pangolin — see StageFrameLibrary) can be swapped and
    /// compared live against the locked camera instead of only ever showing
    /// whatever was hardcoded at launch.
    private var actorPickers: some View {
        Grid(alignment: .leading, horizontalSpacing: 8, verticalSpacing: 4) {
            GridRow {
                Text("foe (az 35)").foregroundStyle(.secondary)
                actorPicker(label: $foeLabel, action: $foeAction) { updateFoe() }
            }
            GridRow {
                Text("party (az 245)").foregroundStyle(.secondary)
                actorPicker(label: $partyLabel, action: $partyAction) { updateParty() }
            }
        }
        .font(.system(.caption2, design: .monospaced))
    }

    private func actorPicker(label: Binding<String>, action: Binding<String>, onChange: @escaping () -> Void) -> some View {
        HStack(spacing: 4) {
            Picker("", selection: label) {
                Text("none").tag("")
                ForEach(library.labels, id: \.self) { Text($0).tag($0) }
            }
            .frame(width: 110)
            .onChange(of: label.wrappedValue) { _, newLabel in
                action.wrappedValue = library.actionsByLabel[newLabel]?.first ?? ""
                onChange()
            }

            Picker("", selection: action) {
                ForEach(library.actionsByLabel[label.wrappedValue] ?? [], id: \.self) { Text($0).tag($0) }
            }
            .frame(width: 220)
            .onChange(of: action.wrappedValue) { _, _ in onChange() }
        }
        .labelsHidden()
    }

    private func updateFoe() {
        let selection = foeLabel.isEmpty ? nil : StageFrameLibrary.Selection(label: foeLabel, action: foeAction, azimuth: 35)
        DungeonStageCameraToolScene.applyBillboard(
            selection, to: foeNode, groundY: DungeonStageCameraToolScene.floorTopY, fallbackTint: nil
        )
        updateLighting()
    }

    private func updateParty() {
        let selection = partyLabel.isEmpty ? nil : StageFrameLibrary.Selection(label: partyLabel, action: partyAction, azimuth: 245)
        DungeonStageCameraToolScene.applyBillboard(
            selection, to: partyNode, groundY: DungeonStageCameraToolScene.floorTopY,
            fallbackTint: NSColor(calibratedRed: 0.6, green: 0.75, blue: 1.0, alpha: 1)
        )
        updateLighting()
    }

    /// Re-applies on every billboard update too, not just when a toggle
    /// flips — a picker change swaps the node's material state underneath
    /// this. Gradient and flat tint are mutually exclusive per material
    /// (both write `shaderModifiers[.fragment]`) — gradient wins when both
    /// are on, since it's the more complete fix.
    private func updateLighting() {
        let foeMaterial = (foeNode.geometry as? SCNPlane)?.firstMaterial
        let partyMaterial = (partyNode.geometry as? SCNPlane)?.firstMaterial
        applyLighting(
            to: foeMaterial, tint: Self.foeLightingTint,
            gradientDir: (foeLightDirX, foeLightDirY), gradientLit: Self.foeGradientLit, gradientShadow: Self.foeGradientShadow
        )
        applyLighting(
            to: partyMaterial, tint: Self.partyLightingTint,
            gradientDir: (partyLightDirX, partyLightDirY), gradientLit: Self.partyGradientLit, gradientShadow: Self.partyGradientShadow
        )
    }

    private func applyLighting(
        to material: SCNMaterial?, tint: NSColor,
        gradientDir: (x: Double, y: Double), gradientLit: NSColor, gradientShadow: NSColor
    ) {
        guard let material else { return }
        if gradientLightMode {
            material.shaderModifiers = [.fragment: gradientShaderModifier(
                dirX: gradientDir.x, dirY: gradientDir.y, strength: gradientStrength, lit: gradientLit, shadow: gradientShadow
            )]
        } else if lightingTintMode, let rgb = tint.usingColorSpace(.deviceRGB) {
            // `SCNMaterial.multiply` turned out to be a no-op here — with
            // `lightingModel = .constant` SceneKit appears to skip it
            // entirely, confirmed 2026-08-27 by it visibly doing nothing. A
            // fragment shader modifier runs regardless of lighting model, so
            // it's the reliable way to tint a billboard's already-composited
            // frame image.
            material.shaderModifiers = [
                .fragment: "_output.color.rgb *= vec3(\(rgb.redComponent), \(rgb.greenComponent), \(rgb.blueComponent));"
            ]
        } else {
            material.shaderModifiers = nil
        }
    }

    /// Fakes a local light *direction* rather than just a color: a linear
    /// gradient across the billboard's own UV space (not screen space —
    /// close enough since the `SCNBillboardConstraint` keeps the plane's
    /// local axes roughly aligned with the camera's), warm/bright toward
    /// `dirX/dirY` (the direction from the sprite's center toward that
    /// corner's real light source) and cooler/dimmer on the opposite side.
    /// `_surface.diffuseTexcoord` is available in a `.fragment` modifier
    /// because the `.surface` stage already populated it earlier in the
    /// pipeline — same trick as reading any other `_surface` field post-hoc.
    private func gradientShaderModifier(dirX: Double, dirY: Double, strength: Double, lit: NSColor, shadow: NSColor) -> String {
        let litRGB = lit.usingColorSpace(.deviceRGB) ?? .white
        let shadowRGB = shadow.usingColorSpace(.deviceRGB) ?? .white
        return """
        vec2 _wkDir = normalize(vec2(\(dirX), \(dirY)) + vec2(0.0001));
        vec2 _wkUV = _surface.diffuseTexcoord.xy - vec2(0.5, 0.5);
        float _wkGrad = clamp(dot(_wkUV, _wkDir) * 2.5 + 0.5, 0.0, 1.0);
        vec3 _wkShadow = vec3(\(shadowRGB.redComponent), \(shadowRGB.greenComponent), \(shadowRGB.blueComponent));
        vec3 _wkLit = vec3(\(litRGB.redComponent), \(litRGB.greenComponent), \(litRGB.blueComponent));
        vec3 _wkRim = mix(_wkShadow, _wkLit, _wkGrad);
        // Clamped, not just interpolated: a strength typed past 1.0
        // extrapolates *beyond* _wkRim instead of blending toward it,
        // which is what washed the sprite out to a flat blue when this
        // was left uncapped (found 2026-08-28).
        vec3 _wkFinal = mix(vec3(1.0), _wkRim, clamp(float(\(strength)), 0.0, 1.0));
        _output.color.rgb *= _wkFinal;
        """
    }

    private func doubleBinding(_ keyPath: WritableKeyPath<OrbitCamera, Double>) -> Binding<Double> {
        Binding(
            get: { orbit[keyPath: keyPath] },
            set: { orbit[keyPath: keyPath] = $0; orbit.apply(to: cameraNode) }
        )
    }

    private func targetBinding(_ keyPath: WritableKeyPath<SCNVector3, CGFloat>) -> Binding<Double> {
        Binding(
            get: { Double(orbit.target[keyPath: keyPath]) },
            set: { orbit.target[keyPath: keyPath] = CGFloat($0); orbit.apply(to: cameraNode) }
        )
    }

    /// Always derived fresh from `orbit`, never a stale snapshot — typing a
    /// value into any field (or dragging) shows up here immediately.
    private var liveSummary: String {
        let p = orbit.computedPosition
        let t = orbit.target
        return String(
            format: "position   x %.2f   y %.2f   z %.2f\ntarget     x %.2f   y %.2f   z %.2f"
                + "\nazimuth %.1f°   elevation %.1f°   radius %.2f   roll %.1f°",
            Double(p.x), Double(p.y), Double(p.z),
            Double(t.x), Double(t.y), Double(t.z),
            orbit.azimuthDegrees, orbit.elevationDegrees, orbit.radius, orbit.rollDegrees
        )
    }

    private func copyToClipboard() {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(liveSummary, forType: .string)
    }

    /// Back to the tool's original starting guess — a clean slate when a
    /// stray edit leaves the camera somewhere odd. Swaps in a genuinely new
    /// camera node rather than just mutating the existing one's properties:
    /// `SceneView`'s `pointOfView` parameter is the same object reference
    /// either way, so an in-place mutation isn't guaranteed to be noticed:
    /// a fresh node identity is unambiguous.
    private func reset() {
        cameraNode.removeFromParentNode()
        orbit = OrbitCamera()
        cameraNode = DungeonStageCameraToolScene.makeStartingCamera(in: scene)
        orbit.apply(to: cameraNode)
    }
}

@MainActor
final class DungeonStageCameraToolWindowController {
    private var window: NSWindow?

    func present() {
        let window = window ?? makeWindow()
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    private func makeWindow() -> NSWindow {
        let hosting = NSHostingController(rootView: DungeonStageCameraToolView())
        let window = NSWindow(contentViewController: hosting)
        window.title = "Dungeon Stage Camera Tool — grey blockout, not real art, 16:9"
        // Deliberately not .resizable — the view inside is a fixed 1280×720,
        // and letting the window resize independently would just reintroduce
        // the "same numbers, different framing" problem this fixes.
        window.styleMask = [.titled, .closable, .miniaturizable]
        window.isReleasedWhenClosed = false
        window.setFrameAutosaveName("DungeonStageCameraToolWindow")
        // Re-asserted after the autosave name: a saved frame from before this
        // became a fixed 16:9 window could otherwise restore a stale size.
        window.setContentSize(NSSize(width: 1280, height: 720))
        if window.frame.origin == .zero { window.center() }
        return window
    }
}
#endif
