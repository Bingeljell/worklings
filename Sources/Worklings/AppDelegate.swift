import AppKit
import CompanionCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let roamingDefaultsKey = "idleRoamingEnabled"
    private static let activityInboxDefaultsKey = "activityInboxEnabled"

    private var companionController: CompanionPanelController?
    private var petSession: PetSession?
    private var presenceMonitor: PresenceMonitor?
    private var activityInboxMonitor: ActivityInboxMonitor?
    private var statusItem: NSStatusItem?
    private var visibilityMenuItem: NSMenuItem?
    private var petHeaderMenuItem: NSMenuItem?
    private var needsMenuItem: NSMenuItem?
    private var warningMenuItem: NSMenuItem?
    private var feedMenuItem: NSMenuItem?
    private var playMenuItem: NSMenuItem?
    private var petMenuItem: NSMenuItem?
    private var sleepMenuItem: NSMenuItem?
    private var focusSessionMenuItem: NSMenuItem?
    private var logWorkMenuItem: NSMenuItem?
    private var roamingMenuItem: NSMenuItem?
    private var activityInboxMenuItem: NSMenuItem?
    private var familyMenuItems: [NSMenuItem] = []
    private var classMenuItems: [NSMenuItem] = []
    private var foodMenuItems: [NSMenuItem] = []
    private var playMenuItems: [NSMenuItem] = []
    #if DEBUG
    private var activityContextMenuItem: NSMenuItem?
    private var isRunningActivitySimulation = false
    #endif

    func applicationDidFinishLaunching(_ notification: Notification) {
        #if DEBUG
        let rateScale = ProcessInfo.processInfo.environment["WORKLINGS_DEBUG_RATE_SCALE"]
            .flatMap(Double.init) ?? 1
        let petSession = PetSession(rates: PetSimulationRates().scaled(by: rateScale))
        #else
        let petSession = PetSession()
        #endif
        let companionController = CompanionPanelController(session: petSession)
        self.petSession = petSession
        self.companionController = companionController

        #if DEBUG
        let idleThreshold = ProcessInfo.processInfo.environment["WORKLINGS_IDLE_THRESHOLD_SECONDS"]
            .flatMap(TimeInterval.init) ?? PresenceEvaluator.defaultIdleThreshold
        let pollInterval = ProcessInfo.processInfo.environment["WORKLINGS_PRESENCE_POLL_SECONDS"]
            .flatMap(TimeInterval.init) ?? 15
        let presenceMonitor = PresenceMonitor(
            session: petSession,
            idleThreshold: idleThreshold,
            pollInterval: pollInterval
        )
        #else
        let presenceMonitor = PresenceMonitor(session: petSession)
        #endif
        self.presenceMonitor = presenceMonitor
        presenceMonitor.start()

        let activityInboxMonitor = ActivityInboxMonitor(session: petSession)
        self.activityInboxMonitor = activityInboxMonitor
        if UserDefaults.standard.bool(forKey: Self.activityInboxDefaultsKey) {
            activityInboxMonitor.start()
        }

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

        let headerItem = NSMenuItem(title: "Loading…", action: nil, keyEquivalent: "")
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

        let renameItem = NSMenuItem(
            title: "Rename…",
            action: #selector(renamePet),
            keyEquivalent: ""
        )
        renameItem.target = self
        menu.addItem(renameItem)

        menu.addItem(makeClassMenuItem())

        menu.addItem(.separator())

        let feedMenuItem = makeFoodMenuItem()
        menu.addItem(feedMenuItem)
        self.feedMenuItem = feedMenuItem

        let playMenuItem = makePlayMenuItem()
        menu.addItem(playMenuItem)
        self.playMenuItem = playMenuItem

        let petItem = NSMenuItem(
            title: "Pet",
            action: #selector(petCompanion),
            keyEquivalent: ""
        )
        petItem.target = self
        menu.addItem(petItem)
        petMenuItem = petItem

        let sleepItem = NSMenuItem(
            title: "Let Sleep",
            action: #selector(sleep),
            keyEquivalent: ""
        )
        sleepItem.target = self
        menu.addItem(sleepItem)
        sleepMenuItem = sleepItem

        menu.addItem(.separator())
        let focusSessionItem = NSMenuItem(
            title: "Start Focus Session",
            action: #selector(toggleFocusSession),
            keyEquivalent: ""
        )
        focusSessionItem.target = self
        menu.addItem(focusSessionItem)
        focusSessionMenuItem = focusSessionItem

        menu.addItem(.separator())
        let logWorkItem = NSMenuItem(
            title: "Log Work",
            action: #selector(logWork),
            keyEquivalent: ""
        )
        logWorkItem.target = self
        menu.addItem(logWorkItem)
        logWorkMenuItem = logWorkItem

        menu.addItem(.separator())
        let roamingItem = NSMenuItem(
            title: "Let Roam",
            action: #selector(toggleRoaming),
            keyEquivalent: ""
        )
        roamingItem.target = self
        menu.addItem(roamingItem)
        roamingMenuItem = roamingItem

        let activityInboxItem = NSMenuItem(
            title: "Accept Work Tool Events",
            action: #selector(toggleActivityInbox),
            keyEquivalent: ""
        )
        activityInboxItem.target = self
        activityInboxItem.toolTip = "Lets connected tools drop activity events into a local inbox folder. Off by default; nothing is read but event kind, source, and time."
        menu.addItem(activityInboxItem)
        activityInboxMenuItem = activityInboxItem

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
            state.family.displayName,
            "Lv.\(state.level) \(state.petClass.displayName)"
        ].joined(separator: " · ")
        needsMenuItem?.title = [
            "Fullness \(Int(state.needs.fullness.rounded()))",
            "Energy \(Int(state.needs.energy.rounded()))",
            "Happiness \(Int(state.needs.happiness.rounded()))",
            "Trust \(Int(state.needs.trust.rounded()))"
        ].joined(separator: " · ")

        warningMenuItem?.title = petSession.persistenceWarning ?? ""
        warningMenuItem?.isHidden = petSession.persistenceWarning == nil

        petMenuItem?.title = "Pet \(state.name)"
        sleepMenuItem?.title = "Let \(state.name) Sleep"

        #if DEBUG
        activityContextMenuItem?.title = Self.describe(petSession.activityContext)
        #endif

        updateRoamingMenuItem()
        updateActivityInboxMenuItem()

        for menuItem in familyMenuItems {
            guard let rawValue = menuItem.representedObject as? String,
                  let family = PetFamily(rawValue: rawValue) else {
                continue
            }
            menuItem.state = family == state.family ? .on : .off
        }

        for menuItem in classMenuItems {
            guard let rawValue = menuItem.representedObject as? String,
                  let petClass = PetClass(rawValue: rawValue) else {
                continue
            }
            menuItem.state = petClass == state.petClass ? .on : .off
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
        apply(
            petSession.workLogAvailability(),
            to: logWorkMenuItem
        )
        updateFocusSessionMenuItem()

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

    private func makeClassMenuItem() -> NSMenuItem {
        let parentItem = NSMenuItem(title: "Choose Class", action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: "Choose Class")

        classMenuItems = PetClass.allCases.map { petClass in
            let item = NSMenuItem(
                title: "\(petClass.displayName) — \(petClass.role)",
                action: #selector(selectClass(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = petClass.rawValue
            submenu.addItem(item)
            return item
        }

        parentItem.submenu = submenu
        return parentItem
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
    private func renamePet() {
        guard let petSession else {
            return
        }

        let alert = NSAlert()
        alert.messageText = "Rename \(petSession.state.name)"
        alert.informativeText = "Choose a new name (up to \(PetState.maximumNameLength) characters)."
        alert.addButton(withTitle: "Rename")
        alert.addButton(withTitle: "Cancel")

        let textField = NSTextField(frame: NSRect(x: 0, y: 0, width: 220, height: 24))
        textField.stringValue = petSession.state.name
        textField.placeholderString = "Name"
        alert.accessoryView = textField
        alert.window.initialFirstResponder = textField

        guard alert.runModal() == .alertFirstButtonReturn else {
            return
        }
        petSession.rename(to: textField.stringValue)
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
    private func selectClass(_ sender: NSMenuItem) {
        guard let rawValue = sender.representedObject as? String,
              let petClass = PetClass(rawValue: rawValue) else {
            return
        }
        petSession?.selectClass(petClass)
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

        let runScriptItem = NSMenuItem(
            title: "Run a Full Day, Sped Up",
            action: #selector(runActivitySimulation),
            keyEquivalent: ""
        )
        runScriptItem.target = self
        submenu.addItem(runScriptItem)
        submenu.addItem(.separator())

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

    /// A scripted rehearsal of a full working day, compressed into seconds
    /// of real time so XP, leveling, and stat growth are visible without
    /// waiting on real clocks. Every timestamp is anchored backward from
    /// `end` (real "now" at kickoff) rather than forward from "now," so the
    /// pet's `lastUpdatedAt` never lands in the future — a forward-anchored
    /// script would leave the pet's condition frozen until real time caught
    /// up to the simulated end point. `workStarted` to `workEnded` is 11
    /// simulated minutes apart, just past Focus Session's minimum
    /// qualifying duration, so its XP grant actually fires.
    @objc
    private func runActivitySimulation() {
        guard let petSession, !isRunningActivitySimulation else {
            return
        }
        isRunningActivitySimulation = true

        let script: [(minutesBeforeEnd: Double, kind: ActivityEventKind)] = [
            (15, .dailyWake),
            (14, .workStarted),
            (3, .workEnded),
            (2, .workLogged),
            (1, .taskCompleted),
            (0, .milestone)
        ]
        let end = Date()

        Task { @MainActor in
            for (minutesBeforeEnd, kind) in script {
                let timestamp = end.addingTimeInterval(-minutesBeforeEnd * 60)
                petSession.receive(SimulatedActivitySource.event(kind, at: timestamp))
                try? await Task.sleep(for: .seconds(1.5))
            }
            isRunningActivitySimulation = false
        }
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

    private func updateFocusSessionMenuItem() {
        guard let petSession else {
            return
        }

        let isActive = petSession.isFocusSessionActive
        focusSessionMenuItem?.title = isActive ? "End Focus Session" : "Start Focus Session"
        focusSessionMenuItem?.state = isActive ? .on : .off
        focusSessionMenuItem?.toolTip = isActive
            ? "Wrap up this focus session."
            : "Tell \(petSession.state.name) you're settling in to work."
    }

    @objc
    private func toggleFocusSession() {
        petSession?.toggleFocusSession()
    }

    @objc
    private func logWork() {
        petSession?.logWork()
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

        let name = petSession?.state.name ?? "your Workling"
        let isEnabled = companionController.isRoamingEnabled
        let isAvailable = companionController.isRoamingAvailable

        if !isAvailable {
            roamingMenuItem?.title = isEnabled
                ? "Disable Roaming (Reduce Motion Active)"
                : "Roaming Unavailable (Reduce Motion Active)"
        } else {
            roamingMenuItem?.title = isEnabled ? "Pause Roaming" : "Let \(name) Roam"
        }

        roamingMenuItem?.state = isEnabled ? .on : .off
        roamingMenuItem?.isEnabled = isAvailable || isEnabled
        roamingMenuItem?.toolTip = isAvailable
            ? "Allow \(name) to wander within the current display."
            : "Roaming pauses while macOS Reduce Motion is enabled."
    }

    @objc
    private func toggleActivityInbox() {
        guard let activityInboxMonitor else {
            return
        }

        let shouldEnable = !UserDefaults.standard.bool(forKey: Self.activityInboxDefaultsKey)
        UserDefaults.standard.set(shouldEnable, forKey: Self.activityInboxDefaultsKey)

        if shouldEnable {
            activityInboxMonitor.start()
        } else {
            activityInboxMonitor.stop()
        }
        updateActivityInboxMenuItem()
    }

    private func updateActivityInboxMenuItem() {
        let isEnabled = UserDefaults.standard.bool(forKey: Self.activityInboxDefaultsKey)
        activityInboxMenuItem?.state = isEnabled ? .on : .off
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
