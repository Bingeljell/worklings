#if DEBUG
import AppKit
import SceneKit
import SwiftUI

/// A throwaway prototype, **not** wired into real gameplay: a grey blockout of
/// the proposed battle stage (party floor → arena gap → foe platform → back
/// wall, per `docs/design/dungeons.md`'s "one scene" blocking) with an
/// orbitable camera, so the actual elevated-3/4 angle can be found by looking
/// rather than guessed from a still image. Debug-only menu item; delete this
/// file once the angle is locked and real environment art starts.
enum DungeonStageSpikeScene {
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
            10, 0.3, 4, at: 0, -0.15, 5,
            color: NSColor(calibratedRed: 0.42, green: 0.34, blue: 0.24, alpha: 1)
        )) // party floor
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
        for side: CGFloat in [-1, 1] {
            scene.rootNode.addChildNode(band(
                0.3, 5, 10, at: side * 5, 2.2, 1,
                color: NSColor(calibratedWhite: 0.07, alpha: 1)
            )) // side walls, just enough to frame the shot
        }

        // A real baked sprite, billboarded, standing on the foe platform — the
        // actual "flat foe in a live 3D stage" question this spike exists to test.
        let foePlane = SCNPlane(width: 2.2, height: 2.2)
        foePlane.firstMaterial?.diffuse.contents = loadSpikeImage("mote-idle") ?? NSColor.systemPink
        foePlane.firstMaterial?.isDoubleSided = true
        foePlane.firstMaterial?.lightingModel = .constant
        let foeNode = SCNNode(geometry: foePlane)
        foeNode.position = SCNVector3(0, 1.5, -2)
        foeNode.constraints = [SCNBillboardConstraint()]
        scene.rootNode.addChildNode(foeNode)

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
    /// `pointOfView`) for `allowsCameraControl`'s drag/scroll manipulator to
    /// actually take hold of it.
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

private func loadSpikeImage(_ resourceName: String) -> NSImage? {
    let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
        ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
    guard let url else { return nil }
    return NSImage(contentsOf: url)
}

/// SceneKit's built-in `allowsCameraControl` manipulator turned out not to update
/// the `pointOfView` node's `position`/`eulerAngles` at all when driven through
/// SwiftUI's `SceneView` — the render visibly orbits, but the node reference this
/// file holds never changes, so there was nothing honest to read back. This
/// hand-rolled orbit (drag = azimuth/elevation, magnify = dolly) drives the same
/// node directly, so what gets read is guaranteed to be what moved the camera.
private struct OrbitCamera {
    var target = SCNVector3(0, 0.5, -1)
    var azimuthDegrees: Double = 0
    var elevationDegrees: Double = 18.4
    var radius: Double = 12.65

    func apply(to node: SCNNode) {
        let az = azimuthDegrees * .pi / 180
        let el = max(min(elevationDegrees, 85), -10) * .pi / 180
        let x = target.x + CGFloat(radius * cos(el) * sin(az))
        let y = target.y + CGFloat(radius * sin(el))
        let z = target.z + CGFloat(radius * cos(el) * cos(az))
        node.position = SCNVector3(x, y, z)
        node.look(at: target)
    }
}

struct DungeonStageSpikeView: View {
    @State private var scene = DungeonStageSpikeScene.build()
    @State private var cameraNode: SCNNode
    @State private var orbit = OrbitCamera()
    @State private var dragStart: OrbitCamera?
    @State private var readout = "Drag to orbit, pinch to dolly. When it looks right, press Read Camera (or space)."

    init() {
        let scene = DungeonStageSpikeScene.build()
        _scene = State(initialValue: scene)
        _cameraNode = State(initialValue: DungeonStageSpikeScene.makeCameraNode(in: scene))
    }

    var body: some View {
        ZStack(alignment: .topLeading) {
            SceneView(scene: scene, pointOfView: cameraNode, options: [])
                .ignoresSafeArea()
                .gesture(
                    DragGesture(minimumDistance: 1)
                        .onChanged { value in
                            let base = dragStart ?? orbit
                            if dragStart == nil { dragStart = orbit }
                            orbit.azimuthDegrees = base.azimuthDegrees - Double(value.translation.width) * 0.3
                            orbit.elevationDegrees = base.elevationDegrees + Double(value.translation.height) * 0.3
                            orbit.apply(to: cameraNode)
                        }
                        .onEnded { _ in dragStart = nil }
                )
                .gesture(
                    MagnificationGesture()
                        .onChanged { scale in
                            orbit.radius = max(3, min(30, orbit.radius / Double(scale)))
                            orbit.apply(to: cameraNode)
                        }
                )

            VStack(alignment: .leading, spacing: 6) {
                Text(readout)
                    .font(.system(.caption, design: .monospaced))
                Button("Read Camera") { captureCamera() }
                    .keyboardShortcut(.space, modifiers: [])
            }
            .padding(10)
            .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 8))
            .foregroundStyle(.white)
            .padding(12)
        }
        .frame(minWidth: 720, minHeight: 520)
        .onAppear { orbit.apply(to: cameraNode) }
    }

    private func captureCamera() {
        let p = cameraNode.position
        let text = String(
            format: "position   x %.2f   y %.2f   z %.2f\nazimuth %.1f°   elevation %.1f°   radius %.2f",
            Double(p.x), Double(p.y), Double(p.z), orbit.azimuthDegrees, orbit.elevationDegrees, orbit.radius
        )
        readout = text
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
    }
}

@MainActor
final class DungeonStageSpikeWindowController {
    private var window: NSWindow?

    func present() {
        let window = window ?? makeWindow()
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    private func makeWindow() -> NSWindow {
        let hosting = NSHostingController(rootView: DungeonStageSpikeView())
        let window = NSWindow(contentViewController: hosting)
        window.title = "Dungeon Stage Spike — throwaway, not real art"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.isReleasedWhenClosed = false
        window.setContentSize(NSSize(width: 900, height: 640))
        window.center()
        return window
    }
}
#endif
