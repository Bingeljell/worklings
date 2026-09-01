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


    /// A material for a room-kit surface: one baked tile texture repeated
    /// across the surface rather than a unique texture per room. The tile is
    /// authored 4×4 world units (see `assets/dungeons/kit/`), so a surface
    /// `w × h` units across repeats it `w/4 × h/4` times.
    ///
    /// This is the whole point of the kit — a 22×9 floor is 4 vertices and one
    /// 1024² texture pair instead of the 21k-vert displaced mesh the Blender
    /// blockout used. The displacement it replaces was never visible in
    /// silhouette at this camera's elevation, so it now lives in the normal map.
    ///
    /// `.repeat` wrapping plus a scaled `contentsTransform` is what tiles it:
    /// SceneKit's box UVs run 0–1 per face, so the transform is the only thing
    /// turning that into multiple repeats.
    static func tiledKitMaterial(
        tile: String,
        surfaceWidth: CGFloat,
        surfaceDepth: CGFloat,
        tileSize: CGFloat = 4
    ) -> SCNMaterial {
        let material = SCNMaterial()
        material.lightingModel = .physicallyBased
        material.diffuse.contents = loadDungeonStageImage("\(tile)_albedo")
        material.normal.contents = loadDungeonStageImage("\(tile)_normal")
        material.roughness.contents = 0.9

        let repeatS = surfaceWidth / tileSize
        let repeatT = surfaceDepth / tileSize
        let transform = SCNMatrix4MakeScale(repeatS, repeatT, 1)
        for property in [material.diffuse, material.normal] {
            property.wrapS = .repeat
            property.wrapT = .repeat
            property.contentsTransform = transform
            // Tiled ground read at a glancing angle is exactly where mip
            // filtering turns to mush — anisotropy is what keeps the far half
            // of the floor from smearing into flat colour.
            property.mipFilter = .linear
            property.maxAnisotropy = 8
        }
        return material
    }

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

        // One flat floor, matching the Blender room's `caveFloor` footprint
        // (44 x 34 world units, top surface at y=0). This replaced the old
        // four-band blockout — party floor / recessed arena gap / raised foe
        // platform / back wall — on 2026-09-01: those bands were staging
        // scaffolding from before the room was real geometry, and the Blender
        // scene they are meant to mirror is a single flat floor. Both
        // combatants now stand on the same surface.
        //
        // Oversized on purpose so it reaches past the frame edges however the
        // shot ends up panned or rotated, rather than getting cropped.
        let floor = band(
            44, 0.3, 34, at: 0, -0.15, 0,
            color: NSColor(calibratedRed: 0.42, green: 0.34, blue: 0.24, alpha: 1)
        )
        floor.name = "bandFloor"
        // SCNBox takes six materials in the order front, right, back, left,
        // top, bottom — only the top face is ever seen, so only it carries the
        // tile; the rest keep the flat blockout colour.
        if let sides = floor.geometry?.firstMaterial {
            floor.geometry?.materials = [
                sides, sides, sides, sides,
                tiledKitMaterial(tile: "floorTile", surfaceWidth: 44, surfaceDepth: 34),
                sides,
            ]
        }
        scene.rootNode.addChildNode(floor)

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

/// Where a combatant stands on the stage floor, and how big it reads there.
///
/// Shared by the real arena and the Dungeon Stage Camera Tool so both place
/// actors at identical coordinates — the tool is only useful as a preview if
/// what it shows is where things actually stand.
///
/// Sizes are 2x the original placeholder sizing — a first pass at "too small"
/// per the 2026-08-21 review. An eyeballed guess, not a calibrated scale.
enum DungeonStageSlot: CaseIterable {
    case party
    case foe

    /// On the floor (y = `DungeonStageActors.floorTopY`); a node's own y is
    /// then raised so its feet, not its center, land on that surface.
    var position: SCNVector3 {
        switch self {
        case .party: SCNVector3(-3, DungeonStageActors.floorTopY, 8.5)
        case .foe: SCNVector3(0, DungeonStageActors.floorTopY, -2)
        }
    }

    /// Edge length of the billboard plane, in world units.
    var size: CGFloat {
        switch self {
        case .party: 5.6
        case .foe: 4.4
        }
    }

    var nodeName: String {
        switch self {
        case .party: "stagePartyActor"
        case .foe: "stageFoeActor"
        }
    }
}

/// Builds the actors that stand on the stage — currently baked-sprite
/// billboards, deliberately shaped so a live 3D model node can take their
/// place without the callers changing.
///
/// Lifted out of the `#if DEBUG` camera tool on 2026-09-01: the real arena
/// needs the same actors as real scene nodes (that, not billboards-vs-3D, is
/// what unblocks impact frames — see dungeons.md), and two implementations of
/// "where does a character stand" would drift the moment one was tuned.
enum DungeonStageActors {
    /// The room's floor height from `DungeonStageScene.build()` — a box
    /// centered at y=-0.15 with height 0.3, so its top surface is y=0. Ground
    /// truth, not eyeballed: actors land on this exact surface regardless of
    /// which character is loaded.
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
    static let groundOffsetFraction: [String: CGFloat] = [
        "tempest-ram": 0.196,
        "forest-flicker": 0.335,
        "pangolin": 0.179,
    ]
    /// Used only for the mote-idle placeholder when no frames are found —
    /// roughly the middle of the observed range above.
    static let defaultGroundOffsetFraction: CGFloat = 0.25


    /// A billboard for `slot`: a camera-facing plane with no texture yet.
    /// Caller supplies the art (see the camera tool's `applyBillboard`), so
    /// this stays free of the dev-only frame library.
    static func makeBillboard(for slot: DungeonStageSlot) -> SCNNode {
        let plane = SCNPlane(width: slot.size, height: slot.size)
        plane.firstMaterial?.isDoubleSided = true
        plane.firstMaterial?.lightingModel = .constant
        let node = SCNNode(geometry: plane)
        node.name = slot.nodeName
        node.position = slot.position
        node.constraints = [SCNBillboardConstraint()]
        return node
    }

    /// The y a node must sit at for the character's feet to land on the floor,
    /// given how far up its own frame that character's ground line falls.
    static func groundedY(for slot: DungeonStageSlot, offsetFraction: CGFloat) -> CGFloat {
        floorTopY + offsetFraction * slot.size
    }

    /// The ground decal for `slot`, positioned at the slot's (x, z) but
    /// deliberately *not* parented to the actor — the actor carries a
    /// `SCNBillboardConstraint` that rotates it to face the camera, and a
    /// child would inherit that rotation, tilting the shadow up off the floor
    /// instead of lying flat. 2026-08-27, first of the "why do these look
    /// pasted on" fixes (see dungeons.md).
    static func makeContactShadow(for slot: DungeonStageSlot) -> SCNNode {
        let p = slot.position
        return makeContactShadow(
            diameter: slot.size * 0.4,
            at: SCNVector3(p.x, floorTopY + 0.006, p.z)
        )
    }

    /// A flat, soft-edged dark oval lying on the ground under a billboard —
    /// without this, a character reads as a sticker floating above the
    /// floor rather than standing on it (2026-08-27 feedback: "they don't
    /// look like they're from the same universe... could be shadows").
    /// Not parented to the billboard — see the call site for why.
    static func makeContactShadow(diameter: CGFloat, at position: SCNVector3) -> SCNNode {
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

}

/// Loads a bundled PNG for use as stage art (billboards, materials) — from
/// the app bundle or the SwiftPM module bundle.
func loadDungeonStageImage(_ resourceName: String) -> NSImage? {
    let url = Bundle.main.url(forResource: resourceName, withExtension: "png")
        ?? Bundle.module.url(forResource: resourceName, withExtension: "png")
    guard let url else { return nil }
    return NSImage(contentsOf: url)
}
