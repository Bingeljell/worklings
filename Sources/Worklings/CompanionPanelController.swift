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
    @Published private(set) var transitionFrame: CompanionTransitionFrame?
    @Published private(set) var isPetVisible = true

    var isTransitioning: Bool {
        transitionFrame != nil
    }

    func startWalking(from origin: CGPoint, to destination: CGPoint) {
        facingDirection = destination.x < origin.x ? .left : .right
        isWalking = true
    }

    func stopWalking() {
        isWalking = false
    }

    func presentTransitionFrame(_ frame: CompanionTransitionFrame) {
        transitionFrame = frame
        isPetVisible = frame.isPetVisible
    }

    func finishTransition(petVisible: Bool) {
        transitionFrame = nil
        isPetVisible = petVisible
    }
}

@MainActor
final class CompanionPanelController {
    private static let panelSize = CGSize(width: 196, height: 196)
    private static let roamingMargin: CGFloat = 24
    private static let animationFramesPerSecond = 30.0
    private static let transitionFrameDuration = Duration.milliseconds(75)

    private let panel: CompanionPanel
    private let session: PetSession
    private let motionState = CompanionMotionState()
    private let hoverSummaryController: HoverSummaryPanelController
    private let carePopoverController: CarePopoverController
    private var hostingView: CompanionHostingView<WorklingPetView>?
    private var hoverTask: Task<Void, Never>?
    private var roamingTask: Task<Void, Never>?
    private var transitionTask: Task<Void, Never>?
    private var roamingSequence = UInt64(Date().timeIntervalSinceReferenceDate / 60)
    private var isPointerInside = false
    private var isUserDragging = false

    private(set) var isRoamingEnabled = false

    private(set) var isVisible = false

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
        guard !isVisible else {
            return
        }

        isVisible = true
        guard shouldAnimateTransitions else {
            finishTransitionImmediately(petVisible: true)
            panel.orderFrontRegardless()
            return
        }

        startTransition(.reveal)
        panel.orderFrontRegardless()
    }

    func hide() {
        guard isVisible else {
            return
        }

        isVisible = false
        isPointerInside = false
        isUserDragging = false
        motionState.stopWalking()
        hideHoverSummary()
        carePopoverController.close()

        guard shouldAnimateTransitions else {
            finishTransitionImmediately(petVisible: false)
            panel.orderOut(nil)
            return
        }

        startTransition(.conceal)
    }

    func selectFamily(_ family: PetFamily) {
        guard family != session.state.family else {
            return
        }

        guard isVisible, panel.isVisible, shouldAnimateTransitions else {
            session.selectFamily(family)
            return
        }

        motionState.stopWalking()
        hideHoverSummary()
        carePopoverController.close()
        startTransition(.familySwap, targetFamily: family)
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
        guard !motionState.isTransitioning, let hostingView else {
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
        guard !motionState.isTransitioning else {
            return
        }

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
            && !motionState.isTransitioning
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

    private var shouldAnimateTransitions: Bool {
        !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
    }

    private func startTransition(
        _ kind: CompanionTransitionKind,
        targetFamily: PetFamily? = nil
    ) {
        transitionTask?.cancel()
        motionState.stopWalking()
        panel.ignoresMouseEvents = true

        let frames = CompanionTransitionPlan.frames(for: kind)
        guard let firstFrame = frames.first else {
            finishTransitionImmediately(petVisible: kind != .conceal)
            return
        }

        motionState.presentTransitionFrame(firstFrame)
        transitionTask = Task { @MainActor [weak self] in
            guard let self else {
                return
            }

            for frame in frames {
                guard !Task.isCancelled else {
                    return
                }

                self.motionState.presentTransitionFrame(frame)
                if frame.shouldSwapFamily, let targetFamily {
                    self.session.selectFamily(targetFamily)
                }

                do {
                    try await Task.sleep(for: Self.transitionFrameDuration)
                } catch {
                    return
                }
            }

            guard !Task.isCancelled else {
                return
            }

            let finalPetVisible = kind != .conceal
            self.motionState.finishTransition(petVisible: finalPetVisible)
            self.panel.ignoresMouseEvents = false
            self.transitionTask = nil

            if kind == .conceal, !self.isVisible {
                self.panel.orderOut(nil)
            }
        }
    }

    private func finishTransitionImmediately(petVisible: Bool) {
        transitionTask?.cancel()
        transitionTask = nil
        motionState.finishTransition(petVisible: petVisible)
        panel.ignoresMouseEvents = false
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
