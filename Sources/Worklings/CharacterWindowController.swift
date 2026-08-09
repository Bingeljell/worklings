import AppKit
import SwiftUI

/// Hosts the Character Screen in its own floating window.
///
/// Deliberately unlike the arena, which is a fixed-aspect stage torn down when a
/// delve ends: this is the home-base hub, so it is **free-resize** (the design's
/// call — the screen is nearly all vector UI, and the one stretchable element is
/// the model bay that becomes live 3D later, so nothing here can pixelate). It
/// also outlives any single interaction: reopening brings back the window you
/// left, in the size and place you left it.
@MainActor
final class CharacterWindowController {
    private static let frameAutosaveName = "CharacterScreenWindow"

    private let session: PetSession
    private var window: NSWindow?

    var isPresenting: Bool { window?.isVisible ?? false }

    init(session: PetSession) {
        self.session = session
    }

    /// Opens the screen, or brings it forward if it is already open. The window is
    /// built once and kept, so tab selection and scroll position survive a close.
    func present() {
        let window = window ?? makeWindow()
        self.window = window

        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    /// The click-the-Workling gesture: a second click on the pet puts the screen
    /// away again, the same way the care popover used to toggle.
    func toggle() {
        if isPresenting {
            close()
        } else {
            present()
        }
    }

    func close() {
        window?.orderOut(nil)
    }

    private func makeWindow() -> NSWindow {
        let hosting = NSHostingController(rootView: CharacterScreenView(session: session))

        let window = NSWindow(contentViewController: hosting)
        window.title = "\(session.state.name) — Character"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.isReleasedWhenClosed = false
        window.level = .floating
        window.setContentSize(NSSize(width: 780, height: 560))
        // Restores the last size and position; only centers the very first time.
        window.setFrameAutosaveName(Self.frameAutosaveName)
        if window.frame.origin == .zero {
            window.center()
        }
        return window
    }
}
