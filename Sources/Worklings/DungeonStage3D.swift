import AppKit
import SceneKit

/// The dungeon battle stage's room geometry — party floor, arena gap, foe
/// platform, back wall — shared by the real in-game arena and the debug
/// Dungeon Stage Camera Tool, so both are always looking at the same room.
/// Not `#if DEBUG`: this is real, shipped rendering. See `docs/design/
/// dungeons.md`'s "The battle stage — camera & staging" for the design.
enum DungeonStageScene {
    /// Every camera onto this stage must share this field of view — a shot
    /// composed at one FOV and rendered at another silently reframes itself.
    static let cameraFieldOfView: CGFloat = 32

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
        // Named so a caller (e.g. the camera tool's backdrop-mode toggle,
        // 2026-08-27) can hide/show the blockout as a group without this
        // function needing to know why.
        let partyFloor = band(
            22, 0.3, 9, at: 0, -0.15, 7,
            color: NSColor(calibratedRed: 0.42, green: 0.34, blue: 0.24, alpha: 1)
        ) // party floor — oversized on purpose, so it reaches past frame edges
          // however the shot ends up panned/rotated rather than getting cropped
        partyFloor.name = "bandPartyFloor"
        scene.rootNode.addChildNode(partyFloor)

        let arenaGap = band(
            10, 0.1, 3, at: 0, -0.35, 1.5,
            color: NSColor(calibratedWhite: 0.03, alpha: 1)
        ) // arena gap, recessed so it reads as its own zone
        arenaGap.name = "bandArenaGap"
        scene.rootNode.addChildNode(arenaGap)

        let foePlatform = band(
            9, 0.6, 3.5, at: 0, 0.1, -2,
            color: NSColor(calibratedRed: 0.22, green: 0.26, blue: 0.28, alpha: 1)
        ) // foe platform, raised
        foePlatform.name = "bandFoePlatform"
        scene.rootNode.addChildNode(foePlatform)

        let backWall = band(
            10, 5, 0.3, at: 0, 2.2, -4,
            color: NSColor(calibratedRed: 0.12, green: 0.13, blue: 0.15, alpha: 1)
        ) // back wall
        backWall.name = "bandBackWall"
        scene.rootNode.addChildNode(backWall)

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

    /// A camera at a fixed position looking at `target`, added to the scene
    /// graph (required for its transform to actually take effect when
    /// rendered — see the Camera Tool's post-mortem on this exact gotcha).
    static func makeCamera(position: SCNVector3, lookingAt target: SCNVector3, in scene: SCNScene) -> SCNNode {
        let camera = SCNCamera()
        camera.fieldOfView = cameraFieldOfView
        let node = SCNNode()
        node.camera = camera
        node.position = position
        node.look(at: target)
        scene.rootNode.addChildNode(node)
        return node
    }
}

/// Loads a bundled PNG for use as stage art (billboards, materials) — from
/// the app bundle or the SwiftPM module bundle.
func loadDungeonStageImage(_ resourceName: String) -> NSImage? {
    let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
        ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
    guard let url else { return nil }
    return NSImage(contentsOf: url)
}
