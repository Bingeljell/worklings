import AppKit
import CompanionCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let roamingDefaultsKey = "idleRoamingEnabled"

    private var companionController: CompanionPanelController?
    private var petSession: PetSession?
    private var statusItem: NSStatusItem?
    private var visibilityMenuItem: NSMenuItem?
    private var petHeaderMenuItem: NSMenuItem?
    private var needsMenuItem: NSMenuItem?
    private var warningMenuItem: NSMenuItem?
    private var feedMenuItem: NSMenuItem?
    private var playMenuItem: NSMenuItem?
    private var sleepMenuItem: NSMenuItem?
    private var roamingMenuItem: NSMenuItem?
    private var familyMenuItems: [NSMenuItem] = []
    private var foodMenuItems: [NSMenuItem] = []
    private var playMenuItems: [NSMenuItem] = []
    #if DEBUG
    private var activityContextMenuItem: NSMenuItem?
    #endif

    func applicationDidFinishLaunching(_ notification: Notification) {
        let petSession = PetSession()
        let companionController = CompanionPanelController(session: petSession)
        self.petSession = petSession
        self.companionController = companionController

        configureStatusItem()
        companionController.setRoamingEnabled(
            UserDefaults.standard.bool(forKey: Self.roamingDefaultsKey)
        )
        companionController.show()
    }

    private func configureStatusItem() {
        let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.title = "🐾"
        statusItem.button?.toolTip = "Worklings"

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
        menu.addItem(makeFamilyMenuItem())
        menu.addItem(.separator())

        let feedMenuItem = makeFoodMenuItem()
        menu.addItem(feedMenuItem)
        self.feedMenuItem = feedMenuItem

        let playMenuItem = makePlayMenuItem()
        menu.addItem(playMenuItem)
        self.playMenuItem = playMenuItem

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
        sleepMenuItem = sleepItem

        menu.addItem(.separator())
        let roamingItem = NSMenuItem(
            title: "Let Pixel Roam",
            action: #selector(toggleRoaming),
            keyEquivalent: ""
        )
        roamingItem.target = self
        menu.addItem(roamingItem)
        roamingMenuItem = roamingItem

        let visibilityItem = NSMenuItem(
            title: "Tuck Away Companion",
            action: #selector(toggleCompanionVisibility),
            keyEquivalent: ""
        )
        visibilityItem.target = self
        menu.addItem(visibilityItem)

        #if DEBUG
        menu.addItem(.separator())
        menu.addItem(makeSimulateActivityMenuItem())
        #endif

        menu.addItem(.separator())

        let quitItem = NSMenuItem(
            title: "Quit Worklings",
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
        let status = petSession.careStatus

        petHeaderMenuItem?.title = [
            state.name,
            presentation.moodLabel,
            state.family.displayName
        ].joined(separator: " · ")
        needsMenuItem?.title = [
            "Fullness \(Int(state.needs.fullness.rounded()))",
            "Energy \(Int(state.needs.energy.rounded()))",
            "Happiness \(Int(state.needs.happiness.rounded()))",
            "Trust \(Int(state.needs.trust.rounded()))"
        ].joined(separator: " · ")

        warningMenuItem?.title = petSession.persistenceWarning ?? ""
        warningMenuItem?.isHidden = petSession.persistenceWarning == nil

        #if DEBUG
        activityContextMenuItem?.title = Self.describe(petSession.activityContext)
        #endif

        updateRoamingMenuItem()

        for menuItem in familyMenuItems {
            guard let rawValue = menuItem.representedObject as? String,
                  let family = PetFamily(rawValue: rawValue) else {
                continue
            }
            menuItem.state = family == state.family ? .on : .off
        }

        apply(
            status.availability(for: .feed, state: state),
            to: feedMenuItem
        )
        apply(
            status.availability(for: .play, state: state),
            to: playMenuItem
        )
        apply(
            status.availability(for: .sleep, state: state),
            to: sleepMenuItem
        )

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

    private func apply(
        _ availability: PetActionAvailability,
        to menuItem: NSMenuItem?
    ) {
        menuItem?.isEnabled = availability.isEnabled
        menuItem?.toolTip = availability.explanation
    }

    private func makeFamilyMenuItem() -> NSMenuItem {
        let parentItem = NSMenuItem(title: "Choose Workling", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Choose Workling")

        familyMenuItems = PetFamily.allCases.map { family in
            let item = NSMenuItem(
                title: familySelectionTitle(for: family),
                action: #selector(selectFamily(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = family.rawValue
            submenu.addItem(item)
            return item
        }

        parentItem.submenu = submenu
        return parentItem
    }

    private func familySelectionTitle(for family: PetFamily) -> String {
        switch family {
        case .wildkin: "Wildkin — Moss-Fox"
        case .elemental: "Elemental — Ember-Newt"
        case .relicborn: "Relicborn — Keyback Pangolin"
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
    private func selectFamily(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let family = PetFamily(rawValue: rawValue) else {
            return
        }
        if let companionController {
            companionController.selectFamily(family)
        } else {
            petSession?.selectFamily(family)
        }
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

    #if DEBUG
    private func makeSimulateActivityMenuItem() -> NSMenuItem {
        let parentItem = NSMenuItem(title: "Simulate Activity", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Simulate Activity")

        let contextItem = NSMenuItem(title: "Context: quiet", action: nil, keyEquivalent: "")
        contextItem.isEnabled = false
        submenu.addItem(contextItem)
        activityContextMenuItem = contextItem
        submenu.addItem(.separator())

        for kind in ActivityEventKind.allCases {
            let item = NSMenuItem(
                title: kind.displayName,
                action: #selector(simulateActivity(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = kind.rawValue
            submenu.addItem(item)
        }

        parentItem.submenu = submenu
        return parentItem
    }

    @objc
    private func simulateActivity(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let kind = ActivityEventKind(rawValue: rawValue) else {
            return
        }
        petSession?.receive(SimulatedActivitySource.event(kind, at: Date()))
    }

    private static func describe(_ context: ActivityContext) -> String {
        var parts: [String] = []
        if context.isWorking {
            parts.append("working")
        }
        if context.isAwaitingInput {
            parts.append("agent waiting")
        }
        if !context.isUserPresent {
            parts.append("user away")
        }
        if parts.isEmpty {
            parts.append("quiet")
        }
        return "Context: " + parts.joined(separator: " · ")
    }
    #endif

    @objc
    private func petCompanion() {
        petSession?.perform(.pet)
    }

    @objc
    private func sleep() {
        petSession?.perform(.sleep)
    }

    @objc
    private func toggleRoaming() {
        guard let companionController else {
            return
        }

        let shouldEnable = !companionController.isRoamingEnabled
        guard !shouldEnable || companionController.isRoamingAvailable else {
            return
        }

        companionController.setRoamingEnabled(shouldEnable)
        UserDefaults.standard.set(shouldEnable, forKey: Self.roamingDefaultsKey)
        updateRoamingMenuItem()
    }

    private func updateRoamingMenuItem() {
        guard let companionController else {
            return
        }

        let isEnabled = companionController.isRoamingEnabled
        let isAvailable = companionController.isRoamingAvailable

        if !isAvailable {
            roamingMenuItem?.title = isEnabled
                ? "Disable Roaming (Reduce Motion Active)"
                : "Roaming Unavailable (Reduce Motion Active)"
        } else {
            roamingMenuItem?.title = isEnabled ? "Pause Roaming" : "Let Pixel Roam"
        }

        roamingMenuItem?.state = isEnabled ? .on : .off
        roamingMenuItem?.isEnabled = isAvailable || isEnabled
        roamingMenuItem?.toolTip = isAvailable
            ? "Allow Pixel to wander within the current display."
            : "Roaming pauses while macOS Reduce Motion is enabled."
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
