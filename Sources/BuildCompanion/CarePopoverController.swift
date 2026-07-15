import AppKit
import SwiftUI

@MainActor
final class CarePopoverController {
    private let popover: NSPopover

    var isShown: Bool {
        popover.isShown
    }

    init(session: PetSession) {
        popover = NSPopover()
        popover.behavior = .transient
        popover.animates = true
        popover.contentSize = CGSize(width: 330, height: 390)
        popover.contentViewController = NSHostingController(
            rootView: PetCareCardView(session: session)
        )
    }

    func toggle(relativeTo anchorView: NSView) {
        if popover.isShown {
            popover.performClose(nil)
        } else {
            show(relativeTo: anchorView)
        }
    }

    func close() {
        popover.performClose(nil)
    }

    private func show(relativeTo anchorView: NSView) {
        let screenFrame = anchorView.window?.screen?.visibleFrame
        let windowMidX = anchorView.window?.frame.midX ?? 0
        let preferredEdge: NSRectEdge = windowMidX > screenFrame?.midX ?? 0
            ? .minX
            : .maxX

        NSApplication.shared.activate(ignoringOtherApps: true)
        popover.show(
            relativeTo: anchorView.bounds,
            of: anchorView,
            preferredEdge: preferredEdge
        )
    }
}
