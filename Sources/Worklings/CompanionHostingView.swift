import AppKit
import SwiftUI

@MainActor
final class CompanionHostingView<Content: View>: NSHostingView<Content> {
    var onClick: (() -> Void)?
    var onHoverChanged: ((Bool) -> Void)?
    var onDragStarted: (() -> Void)?

    private let dragTolerance: CGFloat = 4
    private var companionTrackingArea: NSTrackingArea?
    private var mouseDownScreenLocation: CGPoint?
    private var windowOriginAtMouseDown: CGPoint?
    private var isDraggingCompanion = false

    override func updateTrackingAreas() {
        if let companionTrackingArea {
            removeTrackingArea(companionTrackingArea)
        }

        let trackingArea = NSTrackingArea(
            rect: bounds,
            options: [.activeAlways, .inVisibleRect, .mouseEnteredAndExited],
            owner: self,
            userInfo: nil
        )
        addTrackingArea(trackingArea)
        companionTrackingArea = trackingArea

        super.updateTrackingAreas()
    }

    override func acceptsFirstMouse(for event: NSEvent?) -> Bool {
        true
    }

    override func mouseEntered(with event: NSEvent) {
        guard !isDraggingCompanion else {
            return
        }
        onHoverChanged?(true)
    }

    override func mouseExited(with event: NSEvent) {
        onHoverChanged?(false)
    }

    override func mouseDown(with event: NSEvent) {
        mouseDownScreenLocation = NSEvent.mouseLocation
        windowOriginAtMouseDown = window?.frame.origin
        isDraggingCompanion = false
    }

    override func mouseDragged(with event: NSEvent) {
        guard let window,
              let mouseDownScreenLocation,
              let windowOriginAtMouseDown else {
            return
        }

        let currentLocation = NSEvent.mouseLocation
        let horizontalDelta = currentLocation.x - mouseDownScreenLocation.x
        let verticalDelta = currentLocation.y - mouseDownScreenLocation.y
        let distance = hypot(horizontalDelta, verticalDelta)

        if !isDraggingCompanion, distance > dragTolerance {
            isDraggingCompanion = true
            onHoverChanged?(false)
            onDragStarted?()
        }

        guard isDraggingCompanion else {
            return
        }

        window.setFrameOrigin(
            CGPoint(
                x: windowOriginAtMouseDown.x + horizontalDelta,
                y: windowOriginAtMouseDown.y + verticalDelta
            )
        )
    }

    override func mouseUp(with event: NSEvent) {
        let shouldOpenCareCard = !isDraggingCompanion
        clearPointerState()

        if shouldOpenCareCard {
            onClick?()
        }
    }

    private func clearPointerState() {
        mouseDownScreenLocation = nil
        windowOriginAtMouseDown = nil
        isDraggingCompanion = false
    }
}
