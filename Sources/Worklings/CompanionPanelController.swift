import AppKit
import Combine
import CompanionCore
import SwiftUI

enum CompanionFacingDirection {
    case left
    case right
}

@MainActor
final class CompanionMotionState: ObservableObject {
    @Published private(set) var isWalking = false
    @Published private(set) var facingDirection = CompanionFacingDirection.left

    func startWalking(from origin: CGPoint, to destination: CGPoint) {
        facingDirection = destination.x < origin.x ? .left : .right
        isWalking = true
    }

    func stopWalking() {
        isWalking = false
    }
}

@MainActor
final class CompanionPanelController {
    private static let panelSize = CGSize(width: 196, height: 196)
    private static let roamingMargin: CGFloat = 24
    private static let animationFramesPerSecond = 30.0

    private let panel: CompanionPanel
    private let session: PetSession
    private let motionState = CompanionMotionState()
    private let hoverSummaryController: HoverSummaryPanelController
    private let carePopoverController: CarePopoverController
    private var hostingView: CompanionHostingView<WorklingPetView>?
    private var hoverTask: Task<Void, Never>?
    private var roamingTask: Task<Void, Never>?
    private var roamingSequence = UInt64(Date().timeIntervalSinceReferenceDate / 60)
    private var isPointerInside = false
    private var isUserDragging = false

    private(set) var isRoamingEnabled = false

    var isVisible: Bool {
        panel.isVisible
    }

    var isRoamingAvailable: Bool {
        !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
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
        isPointerInside = false
        isUserDragging = false
        motionState.stopWalking()
        hideHoverSummary()
        carePopoverController.close()
        panel.orderOut(nil)
    }

    func setRoamingEnabled(_ isEnabled: Bool) {
        guard isRoamingEnabled != isEnabled else {
            return
        }

        isRoamingEnabled = isEnabled
        if isEnabled {
            startRoaming()
        } else {
            stopRoaming()
        }
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
            rootView: WorklingPetView(session: session, motion: motionState)
        )
        hostingView.onClick = { [weak self] in
            self?.toggleCareCard()
        }
        hostingView.onHoverChanged = { [weak self] isInside in
            self?.setPointerInside(isInside)
        }
        hostingView.onDragStarted = { [weak self] in
            self?.beginUserDrag()
        }
        hostingView.onDragEnded = { [weak self] in
            self?.finishUserDrag()
        }

        self.hostingView = hostingView
        panel.contentView = hostingView
    }

    private func setPointerInside(_ isInside: Bool) {
        isPointerInside = isInside
        hoverTask?.cancel()

        if isInside {
            motionState.stopWalking()
        }

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

    private func beginUserDrag() {
        isUserDragging = true
        motionState.stopWalking()
        hideHoverSummary()
        carePopoverController.close()
    }

    private func finishUserDrag() {
        isUserDragging = false

        guard let visibleFrame = panel.screen?.visibleFrame
            ?? NSScreen.main?.visibleFrame else {
            return
        }

        panel.setFrameOrigin(
            ScreenPlacement.clampedOrigin(
                proposed: panel.frame.origin,
                windowSize: Self.panelSize,
                visibleFrame: visibleFrame,
                margin: Self.roamingMargin
            )
        )
    }

    private func startRoaming() {
        guard roamingTask == nil else {
            return
        }

        roamingTask = Task { @MainActor [weak self] in
            while !Task.isCancelled {
                guard let self else {
                    return
                }

                guard self.canPerformRoaming else {
                    self.motionState.stopWalking()
                    try? await Task.sleep(for: .milliseconds(500))
                    continue
                }

                let intent = PetRoamingPlanner.intent(
                    sequenceNumber: self.roamingSequence
                )
                self.roamingSequence &+= 1

                try? await Task.sleep(for: .seconds(intent.restDuration))
                guard !Task.isCancelled, self.canPerformRoaming else {
                    continue
                }

                await self.performRoamingStep(intent)
            }
        }
    }

    private func stopRoaming() {
        roamingTask?.cancel()
        roamingTask = nil
        motionState.stopWalking()
    }

    private var canPerformRoaming: Bool {
        isRoamingEnabled
            && panel.isVisible
            && isRoamingAvailable
            && !isPointerInside
            && !isUserDragging
            && !carePopoverController.isShown
    }

    private func performRoamingStep(_ intent: PetRoamingIntent) async {
        guard let visibleFrame = panel.screen?.visibleFrame
            ?? NSScreen.main?.visibleFrame else {
            return
        }

        let origin = panel.frame.origin
        let destination = ScreenPlacement.roamingOrigin(
            from: origin,
            intent: intent,
            windowSize: Self.panelSize,
            visibleFrame: visibleFrame,
            margin: Self.roamingMargin
        )

        guard hypot(destination.x - origin.x, destination.y - origin.y) >= 1 else {
            return
        }

        motionState.startWalking(from: origin, to: destination)
        defer {
            motionState.stopWalking()
        }

        let frameCount = max(
            1,
            Int((intent.travelDuration * Self.animationFramesPerSecond).rounded())
        )

        for frameIndex in 1...frameCount {
            guard !Task.isCancelled, canPerformRoaming else {
                return
            }

            let progress = CGFloat(frameIndex) / CGFloat(frameCount)
            let easedProgress = progress * progress * (3 - 2 * progress)
            panel.setFrameOrigin(
                CGPoint(
                    x: origin.x + (destination.x - origin.x) * easedProgress,
                    y: origin.y + (destination.y - origin.y) * easedProgress
                )
            )

            try? await Task.sleep(for: .milliseconds(33))
        }
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
