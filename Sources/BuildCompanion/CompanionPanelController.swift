import AppKit
import CompanionCore
import SwiftUI

@MainActor
final class CompanionPanelController {
    private static let panelSize = CGSize(width: 168, height: 168)

    private let panel: CompanionPanel

    var isVisible: Bool {
        panel.isVisible
    }

    init() {
        panel = CompanionPanel(
            contentRect: CGRect(origin: .zero, size: Self.panelSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )

        configurePanel()
        placeOnMainScreen()
    }

    func show() {
        panel.orderFrontRegardless()
    }

    func hide() {
        panel.orderOut(nil)
    }

    private func configurePanel() {
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.level = .floating
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        panel.contentView = NSHostingView(rootView: PlaceholderPetView())
    }

    private func placeOnMainScreen() {
        guard let visibleFrame = NSScreen.main?.visibleFrame else {
            panel.center()
            return
        }

        let origin = ScreenPlacement.defaultOrigin(
            windowSize: Self.panelSize,
            visibleFrame: visibleFrame
        )
        panel.setFrameOrigin(origin)
    }
}

private final class CompanionPanel: NSPanel {
    override var canBecomeKey: Bool {
        false
    }

    override var canBecomeMain: Bool {
        false
    }
}
