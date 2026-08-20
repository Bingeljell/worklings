#if DEBUG
import AppKit
import SceneKit
import simd
import SwiftUI

/// A dev tool, **not** wired into real gameplay: a grey blockout of a battle
/// stage (party floor → arena gap → foe platform → back wall, per
/// `docs/design/dungeons.md`'s "one scene" blocking) with an orbitable
/// camera, so a dungeon's actual elevated-3/4 angle and framing can be found
/// by looking rather than guessed from a still image. Kept as a permanent
/// utility — every future dungeon needs its own angle checked the same way.
enum DungeonStageScene {
    static func build() -> SCNScene {
        let scene = SCNScene()
        scene.background.contents = NSColor(calibratedWhite: 0.04, alpha: 1)

        func band(
            _ w: CGFloat, _ h: CGFloat, _ d: CGFloat,
            at x: CGFloat, _ y: CGFloat, _ z: CGFloat,
            color: NSColor
        ) -> SCNNode {
            let node = SCNNode(geometry: SCNBox(width: w, height: h, length: d, chamferRadius: 0))
            node.geometry?.firstMaterial?.diffuse.contents = color
            node.geometry?.firstMaterial?.lightingModel = .physicallyBased
            node.geometry?.firstMaterial?.roughness.contents = 0.85
            node.position = SCNVector3(x, y, z)
            return node
        }

        // Depth bands, near (party) to far (back wall) — see the "Reading this"
        // notes in the blocking diagram for what each one stands in for.
        scene.rootNode.addChildNode(band(
            22, 0.3, 9, at: 0, -0.15, 7,
            color: NSColor(calibratedRed: 0.42, green: 0.34, blue: 0.24, alpha: 1)
        )) // party floor — oversized on purpose, so it reaches past frame edges
           // however the shot ends up panned/rotated rather than getting cropped
        scene.rootNode.addChildNode(band(
            10, 0.1, 3, at: 0, -0.35, 1.5,
            color: NSColor(calibratedWhite: 0.03, alpha: 1)
        )) // arena gap, recessed so it reads as its own zone
        scene.rootNode.addChildNode(band(
            9, 0.6, 3.5, at: 0, 0.1, -2,
            color: NSColor(calibratedRed: 0.22, green: 0.26, blue: 0.28, alpha: 1)
        )) // foe platform, raised
        scene.rootNode.addChildNode(band(
            10, 5, 0.3, at: 0, 2.2, -4,
            color: NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.15, alpha: 1)
        )) // back wall

        // A real baked sprite, billboarded, standing on the foe platform — the
        // actual "flat foe in a live 3D stage" question this tool exists to test.
        let foePlane = SCNPlane(width: 2.2, height: 2.2)
        foePlane.firstMaterial?.diffuse.contents = loadStageImage("mote-idle") ?? NSColor.systemPink
        foePlane.firstMaterial?.isDoubleSided = true
        foePlane.firstMaterial?.lightingModel = .constant
        let foeNode = SCNNode(geometry: foePlane)
        foeNode.position = SCNVector3(0, 1.5, -2)
        foeNode.constraints = [SCNBillboardConstraint()]
        scene.rootNode.addChildNode(foeNode)

        // A placeholder party billboard — same sprite, tinted blue so it
        // reads as "the other side," bigger and further forward since it's
        // meant to sit closer to camera on the party floor. Judging a
        // two-rank standoff composition needs both anchors on screen at
        // once, not just the foe half.
        let partyPlane = SCNPlane(width: 2.8, height: 2.8)
        partyPlane.firstMaterial?.diffuse.contents = loadStageImage("mote-idle") ?? NSColor.systemBlue
        partyPlane.firstMaterial?.multiply.contents = NSColor(calibratedRed: 0.6, green: 0.75, blue: 1.0, alpha: 1)
        partyPlane.firstMaterial?.isDoubleSided = true
        partyPlane.firstMaterial?.lightingModel = .constant
        let partyNode = SCNNode(geometry: partyPlane)
        partyNode.position = SCNVector3(-3, 1.9, 8.5)
        partyNode.constraints = [SCNBillboardConstraint()]
        scene.rootNode.addChildNode(partyNode)

        // Key light — warm, low angle, upper-left, matching the blocking diagram.
        let key = SCNLight()
        key.type = .directional
        key.color = NSColor(calibratedRed: 1.0, green: 0.8, blue: 0.58, alpha: 1)
        key.intensity = 1000
        let keyNode = SCNNode()
        keyNode.light = key
        keyNode.eulerAngles = SCNVector3(-0.785, -0.785, 0) // -45°, -45°
        scene.rootNode.addChildNode(keyNode)

        // Ambient fill — cool, from the depths.
        let fill = SCNLight()
        fill.type = .directional
        fill.color = NSColor(calibratedRed: 0.55, green: 0.78, blue: 0.88, alpha: 1)
        fill.intensity = 320
        let fillNode = SCNNode()
        fillNode.light = fill
        fillNode.eulerAngles = SCNVector3(-0.524, 1.428, 0) // -30°, ~82°
        scene.rootNode.addChildNode(fillNode)

        let ambient = SCNLight()
        ambient.type = .ambient
        ambient.color = NSColor(calibratedWhite: 0.22, alpha: 1)
        let ambientNode = SCNNode()
        ambientNode.light = ambient
        scene.rootNode.addChildNode(ambientNode)

        return scene
    }

    /// A starting guess at the elevated-3/4 angle — a baseline to drag from,
    /// not the answer. Must be added to the scene graph (not just passed as
    /// `pointOfView`) for `OrbitCamera`'s hand-rolled manipulator to actually
    /// take hold of it.
    static func makeCameraNode(in scene: SCNScene) -> SCNNode {
        let camera = SCNCamera()
        camera.fieldOfView = 32
        let node = SCNNode()
        node.camera = camera
        node.position = SCNVector3(0, 4.5, 11)
        node.look(at: SCNVector3(0, 0.5, -1))
        scene.rootNode.addChildNode(node)
        return node
    }
}

private func loadStageImage(_ resourceName: String) -> NSImage? {
    let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
        ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
    guard let url else { return nil }
    return NSImage(contentsOf: url)
}

/// SceneKit's built-in `allowsCameraControl` manipulator turned out not to update
/// the `pointOfView` node's `position`/`eulerAngles` at all when driven through
/// SwiftUI's `SceneView` — the render visibly orbits, but the node reference this
/// file holds never changes, so there was nothing honest to read back. This
/// hand-rolled orbit (drag = azimuth/elevation, scroll = dolly) drives the same
/// node directly, so what gets read is guaranteed to be what moved the camera.
private struct OrbitCamera {
    var target = SCNVector3(0, 0.5, -1)
    var azimuthDegrees: Double = 0
    var elevationDegrees: Double = 18.4
    var radius: Double = 12.65
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

    init() {
        let scene = DungeonStageScene.build()
        _scene = State(initialValue: scene)
        _cameraNode = State(initialValue: DungeonStageScene.makeCameraNode(in: scene))
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
                Text("Drag to orbit · right-drag to pan · scroll to dolly")
                    .font(.system(.caption2, design: .monospaced))
                    .foregroundStyle(.white.opacity(0.7))
                Text(liveSummary)
                    .font(.system(.caption, design: .monospaced))
                parameterFields
                HStack(spacing: 8) {
                    Button("Copy to Clipboard") { copyToClipboard() }
                        .keyboardShortcut(.space, modifiers: [])
                    Button("Reset") { reset() }
                        .keyboardShortcut("r", modifiers: [.command])
                }
            }
            .padding(10)
            .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 8))
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
        cameraNode = DungeonStageScene.makeCameraNode(in: scene)
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
