import CompanionCore
import CoreGraphics

// Bridges AppKit's CoreGraphics geometry to CompanionCore's platform-free
// placement types at the call boundary, keeping the core free of CoreGraphics.

extension CGPoint {
    var placement: PlacementPoint { PlacementPoint(x: Double(x), y: Double(y)) }
}

extension CGSize {
    var placement: PlacementSize { PlacementSize(width: Double(width), height: Double(height)) }
}

extension CGRect {
    var placement: PlacementRect {
        PlacementRect(
            x: Double(origin.x),
            y: Double(origin.y),
            width: Double(width),
            height: Double(height)
        )
    }
}

extension PlacementPoint {
    var cgPoint: CGPoint { CGPoint(x: x, y: y) }
}
