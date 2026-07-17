import AppKit
import CompanionCore
import SwiftUI

@MainActor
final class CompanionPanelController {
    private static let panelSize = CGSize(width: 196, height: 196)

    private let panel: CompanionPanel
    private let session: PetSession
    private let hoverSummaryController: HoverSummaryPanelController
    private let carePopoverController: CarePopoverController
    private var hostingView: CompanionHostingView<WildkinPetView>?
    private var hoverTask: Task<Void, Never>?
    private var isPointerInside = false

    var isVisible: Bool {
        panel.isVisible
    }

    init(session: PetSession) {
        self.session = session
        hoverSummaryController = HoverSummaryPanelController()
        carePopoverController = CarePopoverController(session: session)
        panel = CompanionPanel(
            contentRect: CGRect(origin: .zero, size: Self.panelSize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )

        configurePanel(session: session)
        placeOnMainScreen()
    }

    func show() {
        panel.orderFrontRegardless()
    }

    func hide() {
        hideHoverSummary()
        carePopoverController.close()
        panel.orderOut(nil)
    }

    private func configurePanel(session: PetSession) {
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.level = .floating
        panel.isMovableByWindowBackground = false
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        let hostingView = CompanionHostingView(
            rootView: WildkinPetView(session: session)
        )
        hostingView.onClick = { [weak self] in
            self?.toggleCareCard()
        }
        hostingView.onHoverChanged = { [weak self] isInside in
            self?.setPointerInside(isInside)
        }
        hostingView.onDragStarted = { [weak self] in
            self?.hideHoverSummary()
            self?.carePopoverController.close()
        }

        self.hostingView = hostingView
        panel.contentView = hostingView
    }

    private func setPointerInside(_ isInside: Bool) {
        isPointerInside = isInside
        hoverTask?.cancel()

        guard isInside, !carePopoverController.isShown else {
            hideHoverSummary()
            return
        }

        hoverTask = Task { @MainActor [weak self] in
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled,
                  let self,
                  self.isPointerInside,
                  !self.carePopoverController.isShown else {
                return
            }

            self.hoverSummaryController.show(
                summary: self.session.careStatus.hoverSummary,
                relativeTo: self.panel
            )
        }
    }

    private func toggleCareCard() {
        guard let hostingView else {
            return
        }

        hideHoverSummary()
        carePopoverController.toggle(relativeTo: hostingView)
    }

    private func hideHoverSummary() {
        hoverTask?.cancel()
        hoverTask = nil
        hoverSummaryController.hide()
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
