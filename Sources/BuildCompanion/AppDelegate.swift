import AppKit
import CompanionCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private var companionController: CompanionPanelController?
    private var petSession: PetSession?
    private var statusItem: NSStatusItem?
    private var visibilityMenuItem: NSMenuItem?
    private var petHeaderMenuItem: NSMenuItem?
    private var needsMenuItem: NSMenuItem?
    private var warningMenuItem: NSMenuItem?
    private var foodMenuItems: [NSMenuItem] = []
    private var playMenuItems: [NSMenuItem] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        let petSession = PetSession()
        let companionController = CompanionPanelController(session: petSession)
        self.petSession = petSession
        self.companionController = companionController

        configureStatusItem()
        companionController.show()
    }

    private func configureStatusItem() {
        let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.title = "🐾"
        statusItem.button?.toolTip = "Build Companion"

        let menu = NSMenu()
        menu.delegate = self

        let headerItem = NSMenuItem(title: "Pixel — Content", action: nil, keyEquivalent: "")
        headerItem.isEnabled = false
        menu.addItem(headerItem)
        petHeaderMenuItem = headerItem

        let needsItem = NSMenuItem(title: "Needs", action: nil, keyEquivalent: "")
        needsItem.isEnabled = false
        menu.addItem(needsItem)
        needsMenuItem = needsItem

        let warningItem = NSMenuItem(title: "", action: nil, keyEquivalent: "")
        warningItem.isEnabled = false
        warningItem.isHidden = true
        menu.addItem(warningItem)
        warningMenuItem = warningItem

        menu.addItem(.separator())
        menu.addItem(makeFoodMenuItem())
        menu.addItem(makePlayMenuItem())

        let petItem = NSMenuItem(
            title: "Pet Pixel",
            action: #selector(petCompanion),
            keyEquivalent: ""
        )
        petItem.target = self
        menu.addItem(petItem)

        let sleepItem = NSMenuItem(
            title: "Let Pixel Sleep",
            action: #selector(sleep),
            keyEquivalent: ""
        )
        sleepItem.target = self
        menu.addItem(sleepItem)

        menu.addItem(.separator())
        let visibilityItem = NSMenuItem(
            title: "Tuck Away Companion",
            action: #selector(toggleCompanionVisibility),
            keyEquivalent: ""
        )
        visibilityItem.target = self
        menu.addItem(visibilityItem)
        menu.addItem(.separator())

        let quitItem = NSMenuItem(
            title: "Quit Build Companion",
            action: #selector(quit),
            keyEquivalent: "q"
        )
        quitItem.target = self
        menu.addItem(quitItem)

        statusItem.menu = menu
        self.statusItem = statusItem
        visibilityMenuItem = visibilityItem
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        guard let petSession else {
            return
        }

        petSession.advance()
        let state = petSession.state
        let presentation = PetPresentation.make(state: state, reaction: petSession.reaction)

        petHeaderMenuItem?.title = "\(state.name) — \(presentation.moodLabel)"
        needsMenuItem?.title = [
            "Hunger \(Int(state.needs.hunger.rounded()))",
            "Energy \(Int(state.needs.energy.rounded()))",
            "Happy \(Int(state.needs.happiness.rounded()))",
            "Trust \(Int(state.needs.trust.rounded()))"
        ].joined(separator: " · ")

        warningMenuItem?.title = petSession.persistenceWarning ?? ""
        warningMenuItem?.isHidden = petSession.persistenceWarning == nil

        for menuItem in foodMenuItems {
            guard let rawValue = menuItem.representedObject as? String,
                  let food = PetFood(rawValue: rawValue) else {
                continue
            }
            menuItem.state = food == state.preferences.favouriteFood ? .on : .off
        }

        for menuItem in playMenuItems {
            guard let rawValue = menuItem.representedObject as? String,
                  let activity = PetPlayActivity(rawValue: rawValue) else {
                continue
            }
            menuItem.state = activity == state.preferences.favouritePlayActivity ? .on : .off
        }
    }

    private func makeFoodMenuItem() -> NSMenuItem {
        let parentItem = NSMenuItem(title: "Feed", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Feed")

        foodMenuItems = PetFood.allCases.map { food in
            let item = NSMenuItem(
                title: food.displayName,
                action: #selector(feed(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = food.rawValue
            submenu.addItem(item)
            return item
        }

        parentItem.submenu = submenu
        return parentItem
    }

    private func makePlayMenuItem() -> NSMenuItem {
        let parentItem = NSMenuItem(title: "Play", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Play")

        playMenuItems = PetPlayActivity.allCases.map { activity in
            let item = NSMenuItem(
                title: activity.displayName,
                action: #selector(play(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = activity.rawValue
            submenu.addItem(item)
            return item
        }

        parentItem.submenu = submenu
        return parentItem
    }

    @objc
    private func feed(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let food = PetFood(rawValue: rawValue) else {
            return
        }
        petSession?.perform(.feed(food))
    }

    @objc
    private func play(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let activity = PetPlayActivity(rawValue: rawValue) else {
            return
        }
        petSession?.perform(.play(activity))
    }

    @objc
    private func petCompanion() {
        petSession?.perform(.pet)
    }

    @objc
    private func sleep() {
        petSession?.perform(.sleep)
    }

    @objc
    private func toggleCompanionVisibility() {
        guard let companionController else {
            return
        }

        if companionController.isVisible {
            companionController.hide()
            visibilityMenuItem?.title = "Wake Companion"
        } else {
            companionController.show()
            visibilityMenuItem?.title = "Tuck Away Companion"
        }
    }

    @objc
    private func quit() {
        NSApplication.shared.terminate(nil)
    }
}
