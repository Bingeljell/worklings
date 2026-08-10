import AppKit
import CompanionCore

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let roamingDefaultsKey = "idleRoamingEnabled"
    /// Set once the user has acknowledged the tool-connection consent dialog for a
    /// given tool, so it is shown on that tool's first connect and never again.
    /// Remembered per tool: each tool edits a different file, so its "exact file
    /// being changed" disclosure must be shown the first time you connect *it*.
    private static func toolConsentAcknowledgedKey(_ toolKey: String) -> String {
        "toolConnectionConsentAcknowledged.\(toolKey)"
    }

    private var companionController: CompanionPanelController?
    private var combatPanelController: CombatPanelController?
    private var characterWindowController: CharacterWindowController?
    private var petSession: PetSession?
    private var presenceMonitor: PresenceMonitor?
    private var activityInboxMonitor: ActivityInboxMonitor?
    private var gitCommitWatcher: GitCommitWatcher?
    // note: no activity-inbox menu item — connecting a tool is the opt-in
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
    private var dungeonMenuItem: NSMenuItem?
    private var roamingMenuItem: NSMenuItem?
    private var combatAudioMenuItem: NSMenuItem?
    private var connectedReposMenuItem: NSMenuItem?
    private var connectedReposMenu: NSMenu?
    private var connectClaudeCodeMenuItem: NSMenuItem?
    private var connectCodexMenuItem: NSMenuItem?
    private var disconnectAllToolsMenuItem: NSMenuItem?
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
        let characterWindowController = CharacterWindowController(session: petSession)
        self.petSession = petSession
        self.companionController = companionController
        self.combatPanelController = CombatPanelController()
        self.characterWindowController = characterWindowController

        // Clicking the Workling opens its Character Screen — one window, whether
        // you got there by the pet or by the menu, so the two can't disagree.
        companionController.onClick = { [weak characterWindowController] in
            characterWindowController?.toggle()
        }

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

        // Watch the inbox so files never accumulate. Connecting a tool is itself
        // the opt-in (the same way connecting a repo is), so there is no separate
        // global toggle — any event that arrives is delivered.
        let activityInboxMonitor = ActivityInboxMonitor(session: petSession)
        self.activityInboxMonitor = activityInboxMonitor
        activityInboxMonitor.start()

        // Connecting a repository is itself the opt-in, so the git watcher runs
        // whenever there are connected repos — no separate global toggle.
        let gitCommitWatcher = GitCommitWatcher(session: petSession)
        self.gitCommitWatcher = gitCommitWatcher
        gitCommitWatcher.start()

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

        // The hub, reachable two ways: here, and by clicking the Workling itself.
        let characterItem = NSMenuItem(
            title: "Character Screen",
            action: #selector(openCharacterScreen),
            keyEquivalent: ""
        )
        characterItem.target = self
        characterItem.toolTip = "Gear, stats, and care — or just click your Workling."
        menu.addItem(characterItem)

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
        // A single "descend" that runs the whole Cache Warren delve — briefing,
        // the encounter chain with press-your-luck between fights, then the exit.
        let dungeonItem = NSMenuItem(
            title: "Descend into the Cache Warren",
            action: #selector(descendIntoCacheWarren),
            keyEquivalent: ""
        )
        dungeonItem.target = self
        menu.addItem(dungeonItem)
        dungeonMenuItem = dungeonItem

        let audioItem = NSMenuItem(
            title: "Combat Sound",
            action: #selector(toggleCombatAudio),
            keyEquivalent: ""
        )
        audioItem.target = self
        audioItem.state = CombatAudio.shared.isMuted ? .off : .on
        audioItem.toolTip = "Toggle the dungeon's music and sound effects."
        menu.addItem(audioItem)
        combatAudioMenuItem = audioItem

        menu.addItem(makeCombatVolumeMenuItem())

        menu.addItem(.separator())
        let roamingItem = NSMenuItem(
            title: "Let Roam",
            action: #selector(toggleRoaming),
            keyEquivalent: ""
        )
        roamingItem.target = self
        menu.addItem(roamingItem)
        roamingMenuItem = roamingItem

        let connectedReposItem = NSMenuItem(title: "Connected Repos", action: nil, keyEquivalent: "")
        let connectedReposSubmenu = NSMenu()
        connectedReposItem.submenu = connectedReposSubmenu
        connectedReposItem.toolTip = "Watch git repositories you choose; each commit cheers your Workling on. Only commit identifiers are read — never messages, diffs, or file paths."
        menu.addItem(connectedReposItem)
        connectedReposMenuItem = connectedReposItem
        connectedReposMenu = connectedReposSubmenu

        let connectClaudeItem = NSMenuItem(
            title: "Connect Claude Code",
            action: #selector(toggleClaudeCodeConnection),
            keyEquivalent: ""
        )
        connectClaudeItem.target = self
        connectClaudeItem.toolTip = "Wire Claude Code's lifecycle hooks to Worklings by editing ~/.claude/settings.json. Your existing settings and hooks are preserved and backed up first; disconnecting removes only Worklings' entries."
        menu.addItem(connectClaudeItem)
        connectClaudeCodeMenuItem = connectClaudeItem

        let connectCodexItem = NSMenuItem(
            title: "Connect Codex",
            action: #selector(toggleCodexConnection),
            keyEquivalent: ""
        )
        connectCodexItem.target = self
        connectCodexItem.toolTip = "Wire Codex's [hooks] to Worklings via a dedicated ~/.codex/hooks.json — your config.toml is never touched. Disconnecting removes only Worklings' entries."
        menu.addItem(connectCodexItem)
        connectCodexMenuItem = connectCodexItem

        let disconnectAllItem = NSMenuItem(
            title: "Disconnect All Tools",
            action: #selector(disconnectAllTools),
            keyEquivalent: ""
        )
        disconnectAllItem.target = self
        disconnectAllItem.toolTip = "Remove every Worklings hook from Claude Code and Codex in one step (each config is backed up first). Use this before moving or deleting Worklings so no stale hooks are left behind."
        menu.addItem(disconnectAllItem)
        disconnectAllToolsMenuItem = disconnectAllItem

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

        let forgetGearItem = NSMenuItem(
            title: "Forget Acquired Gear",
            action: #selector(forgetAcquiredGear),
            keyEquivalent: ""
        )
        forgetGearItem.target = self
        forgetGearItem.toolTip =
            "Debug: drop back to the starter item so boss drops can be earned again. "
            + "Keeps name, needs, XP, class, and family."
        menu.addItem(forgetGearItem)
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
            PetPresentation.levelClassLabel(for: state)
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
        updateConnectedReposMenu()
        updateToolConnectionItems()

        syncCheckmarks(familyMenuItems, selectedRawValue: state.family.rawValue)
        syncCheckmarks(classMenuItems, selectedRawValue: state.petClass.rawValue)

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
        updateDungeonMenuItem(state: state)

        syncCheckmarks(foodMenuItems, selectedRawValue: state.preferences.favouriteFood.rawValue)
        syncCheckmarks(
            playMenuItems,
            selectedRawValue: state.preferences.favouritePlayActivity.rawValue
        )
    }

    private func syncCheckmarks(_ items: [NSMenuItem], selectedRawValue: String) {
        for item in items {
            item.state = (item.representedObject as? String) == selectedRawValue ? .on : .off
        }
    }

    private func apply(
        _ availability: PetActionAvailability,
        to menuItem: NSMenuItem?
    ) {
        menuItem?.isEnabled = availability.isEnabled
        menuItem?.toolTip = availability.explanation
    }

    private func updateDungeonMenuItem(state: PetState) {
        guard let dungeonMenuItem, let petSession else { return }
        switch petSession.delveBlock {
        case nil:
            dungeonMenuItem.isEnabled = true
            dungeonMenuItem.toolTip = "Descend into the Cache Warren for a fight."
        case .belowGateLevel(let required):
            dungeonMenuItem.isEnabled = false
            dungeonMenuItem.toolTip = "Reach level \(required) to unlock the Cache Warren."
        case .needsCare:
            dungeonMenuItem.isEnabled = false
            dungeonMenuItem.toolTip = "\(state.name) needs care before delving."
        }
    }

    #if DEBUG
    @objc private func forgetAcquiredGear() {
        petSession?.debugForgetAcquiredItems()
    }
    #endif

    @objc private func openCharacterScreen() {
        characterWindowController?.present()
    }

    @objc private func descendIntoCacheWarren() {
        guard let petSession, petSession.canEnterDelve else { return }
        // Seed from the moment of entry so each delve plays out a little
        // differently; the delve itself is deterministic from this seed.
        let seed = UInt64(bitPattern: Int64(Date().timeIntervalSinceReferenceDate.bitPattern))
        // The companion leaves the desktop (a smoke conceal) and reappears in the
        // arena; bring it back when the delve ends, however it ends.
        let wasVisible = companionController?.isVisible ?? false
        companionController?.hide()
        combatPanelController?.present(
            session: petSession, seed: seed,
            onDismiss: { [weak self] in
                if wasVisible { self?.companionController?.show() }
            }
        )
    }

    /// The one submenu builder behind every raw-representable choice menu
    /// (family, class, food, play), so wiring changes — target retention,
    /// represented objects, accessibility — happen in exactly one place.
    private func makeChoiceMenuItem<Choice: CaseIterable & RawRepresentable>(
        title: String,
        action: Selector,
        titleFor: (Choice) -> String,
        isEnabled: (Choice) -> Bool = { _ in true }
    ) -> (parent: NSMenuItem, items: [NSMenuItem]) where Choice.RawValue == String {
        let parentItem = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let submenu = NSMenu(title: title)
        // AppKit's automatic validation would re-enable anything with a target and
        // an action, which is exactly what `isEnabled` is here to override.
        submenu.autoenablesItems = false

        let items = Choice.allCases.map { choice in
            let item = NSMenuItem(title: titleFor(choice), action: action, keyEquivalent: "")
            item.target = self
            item.representedObject = choice.rawValue
            item.isEnabled = isEnabled(choice)
            submenu.addItem(item)
            return item
        }

        parentItem.submenu = submenu
        return (parentItem, items)
    }

    private func makeFamilyMenuItem() -> NSMenuItem {
        let (parent, items) = makeChoiceMenuItem(
            title: "Choose Workling",
            action: #selector(selectFamily(_:)),
            titleFor: familySelectionTitle(for:),
            // A family with no sprite sheet is listed but not pickable: the lane
            // stays visible so the roster reads as five, and it un-greys on its
            // own the moment its art is baked.
            isEnabled: \.hasArt
        )
        familyMenuItems = items
        return parent
    }

    private func familySelectionTitle(for family: PetFamily) -> String {
        let name = switch family {
        case .wildkin: "Wildkin — Moss-Fox"
        case .elemental: "Elemental — Ember-Newt"
        case .relicborn: "Relicborn — Keyback Pangolin"
        case .glitchkin: "Glitchkin — Sparktail"
        case .bloomglass: "Bloomglass — Starpetal Fawn"
        }
        // A family exists mechanically before its sprite sheet is baked, so the
        // menu says so rather than quietly offering a placeholder-glyph pet.
        return family.hasArt ? name : "\(name) (coming soon)"
    }

    private func makeClassMenuItem() -> NSMenuItem {
        let (parent, items) = makeChoiceMenuItem(
            title: "Choose Class",
            action: #selector(selectClass(_:)),
            titleFor: { (petClass: PetClass) in "\(petClass.displayName) — \(petClass.role)" }
        )
        classMenuItems = items
        return parent
    }

    private func makeFoodMenuItem() -> NSMenuItem {
        let (parent, items) = makeChoiceMenuItem(
            title: "Feed",
            action: #selector(feed(_:)),
            titleFor: { (food: PetFood) in food.displayName }
        )
        foodMenuItems = items
        return parent
    }

    private func makePlayMenuItem() -> NSMenuItem {
        let (parent, items) = makeChoiceMenuItem(
            title: "Play",
            action: #selector(play(_:)),
            titleFor: { (activity: PetPlayActivity) in activity.displayName }
        )
        playMenuItems = items
        return parent
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
              let family = PetFamily(rawValue: rawValue),
              // The menu already greys these out; this keeps the invariant true
              // rather than merely unreachable through the UI.
              family.hasArt else {
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
    private func toggleCombatAudio() {
        CombatAudio.shared.isMuted.toggle()
        combatAudioMenuItem?.state = CombatAudio.shared.isMuted ? .off : .on
    }

    /// A labelled volume slider hosted as a custom-view menu item, sitting under
    /// the "Combat Sound" toggle so all audio controls live together.
    private func makeCombatVolumeMenuItem() -> NSMenuItem {
        let container = NSView(frame: NSRect(x: 0, y: 0, width: 210, height: 26))

        let label = NSTextField(labelWithString: "Volume")
        label.font = .menuFont(ofSize: 12)
        label.textColor = .secondaryLabelColor
        label.frame = NSRect(x: 20, y: 4, width: 52, height: 18)
        container.addSubview(label)

        let slider = NSSlider(
            value: Double(CombatAudio.shared.masterVolume),
            minValue: 0, maxValue: 1,
            target: self, action: #selector(changeCombatVolume(_:))
        )
        slider.isContinuous = true
        slider.frame = NSRect(x: 74, y: 3, width: 118, height: 20)
        container.addSubview(slider)

        let item = NSMenuItem()
        item.view = container
        return item
    }

    @objc
    private func changeCombatVolume(_ sender: NSSlider) {
        CombatAudio.shared.masterVolume = Float(sender.doubleValue)
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

    /// Rebuilds the connected-repos submenu from the registry each time the menu
    /// opens: a "Connect a Repo…" item, then one disconnect item per repo.
    private func updateConnectedReposMenu() {
        guard let submenu = connectedReposMenu else {
            return
        }
        submenu.removeAllItems()

        let connectItem = NSMenuItem(
            title: "Connect a Repo…",
            action: #selector(connectGitRepo),
            keyEquivalent: ""
        )
        connectItem.target = self
        submenu.addItem(connectItem)

        let paths = gitCommitWatcher?.connectedRepoPaths() ?? []
        if !paths.isEmpty {
            submenu.addItem(.separator())
            for path in paths {
                let name = URL(fileURLWithPath: path).lastPathComponent
                let item = NSMenuItem(
                    title: "Disconnect \(name)",
                    action: #selector(disconnectGitRepo(_:)),
                    keyEquivalent: ""
                )
                item.target = self
                item.representedObject = path
                item.toolTip = path
                submenu.addItem(item)
            }
        }

        connectedReposMenuItem?.title = paths.isEmpty
            ? "Connected Repos"
            : "Connected Repos (\(paths.count))"
    }

    @objc
    private func connectGitRepo() {
        guard let gitCommitWatcher else {
            return
        }

        NSApp.activate(ignoringOtherApps: true)
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.prompt = "Connect"
        panel.message = "Choose a git repository to watch. Each commit will cheer on your Workling."

        guard panel.runModal() == .OK, let url = panel.url else {
            return
        }

        // Resolve/connect off-main so a slow repo never freezes the click; the
        // result comes back on the main actor for the alert.
        Task { [weak self] in
            let connected = await gitCommitWatcher.connect(path: url.path)
            guard !connected, self != nil else {
                return
            }
            let alert = NSAlert()
            alert.messageText = "Not a git repository"
            alert.informativeText = "“\(url.lastPathComponent)” doesn’t look like a git repository. Choose the folder that contains its .git directory."
            alert.runModal()
        }
    }

    @objc
    private func disconnectGitRepo(_ sender: NSMenuItem) {
        guard let path = sender.representedObject as? String else {
            return
        }
        gitCommitWatcher?.disconnect(path: path)
    }

    private func claudeCodeConnector() -> ToolConnector {
        ToolConnector(
            configURL: FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent(".claude/settings.json"),
            adapterPath: AdapterLocator.path(for: "worklings-claude-code-activity-hook"),
            mappings: HookConfigMerger.claudeCodeMappings,
            // Exec form: no shell, so a path with a space or metacharacter is
            // passed to the executable verbatim.
            style: .execForm
        )
    }

    private func codexConnector() -> ToolConnector {
        ToolConnector(
            configURL: FileManager.default.homeDirectoryForCurrentUser
                .appendingPathComponent(".codex/hooks.json"),
            adapterPath: AdapterLocator.path(for: "worklings-codex-activity-hook"),
            mappings: HookConfigMerger.codexMappings,
            // Codex documents only the shell form, so the path is single-quoted.
            style: .shellForm
        )
    }

    private func updateToolConnectionItems() {
        updateToolItem(connectClaudeCodeMenuItem, connector: claudeCodeConnector(), named: "Claude Code")
        updateToolItem(connectCodexMenuItem, connector: codexConnector(), named: "Codex")
        // "Disconnect All Tools" stays always-clickable; when nothing is wired it
        // reports "Nothing to disconnect" rather than being greyed out.
    }

    /// Reflects a tool's connection state in its menu item and returns it. A
    /// live connection shows a checkmark; a stale one (the adapter the hook
    /// points at is gone — the app was moved or deleted) is surfaced as an
    /// explicit "Reconnect … — adapter moved" so a single click repoints it.
    @discardableResult
    private func updateToolItem(_ item: NSMenuItem?, connector: ToolConnector, named name: String) -> ToolConnector.ConnectionState {
        let state = connector.connectionState()
        switch state {
        case .notConnected:
            item?.title = "Connect \(name)"
            item?.state = .off
        case .live:
            item?.title = "Connect \(name)"
            item?.state = .on
        case .stale:
            item?.title = "Reconnect \(name) — adapter moved"
            item?.state = .off
        case .unknown:
            item?.title = "Connect \(name) — can’t read config"
            item?.state = .off
        }
        return state
    }

    @objc
    private func toggleClaudeCodeConnection() {
        toggleConnection(claudeCodeConnector(), named: "Claude Code", consentKey: "claude-code")
    }

    @objc
    private func toggleCodexConnection() {
        // Codex will not run newly added hooks until they are reviewed and
        // trusted, so writing the file is not the whole story — tell the user.
        toggleConnection(
            codexConnector(),
            named: "Codex",
            consentKey: "codex",
            postConnectNote: "Codex won’t run new hooks until you approve them. In Codex, run /hooks and trust the Worklings hooks to activate them."
        )
    }

    /// Connects or disconnects a tool by writing its config. On failure the
    /// connector has already left the file untouched, so we only surface the
    /// reason. The first connect of any tool shows a one-time informed-consent
    /// dialog the user can decline. `postConnectNote`, if given, is shown after a
    /// successful connect (e.g. a required approval step).
    private func toggleConnection(_ connector: ToolConnector, named name: String, consentKey: String, postConnectNote: String? = nil) {
        do {
            switch connector.connectionState() {
            case .live:
                _ = try connector.disconnect()
            case .unknown:
                // We can't read or parse the config, so we can't safely say
                // whether our hooks are there — don't write anything, just
                // explain. (Writing would fail closed anyway.)
                NSApp.activate(ignoringOtherApps: true)
                let alert = NSAlert()
                alert.messageText = "Can’t read \(name)’s config"
                alert.informativeText = "Worklings couldn’t read or parse:\n\(connector.configURL.path)\n\nSo it can’t tell whether its hooks are there. Fix or remove that file, then try again."
                alert.runModal()
            case .notConnected, .stale:
                // Informed consent before this tool's first write; if the user
                // declines, nothing is written.
                guard confirmToolConnectionConsent(toolName: name, configPath: connector.configURL.path, consentKey: consentKey) else {
                    return
                }
                // A stale hook (adapter moved/deleted) is repaired the same way
                // it is first written: connect() strips our old entries and
                // writes fresh ones pointing at the current adapter.
                _ = try connector.connect()
                if let postConnectNote {
                    NSApp.activate(ignoringOtherApps: true)
                    let alert = NSAlert()
                    alert.messageText = "Connected \(name)"
                    alert.informativeText = postConnectNote
                    alert.runModal()
                }
            }
        } catch {
            NSApp.activate(ignoringOtherApps: true)
            let alert = NSAlert()
            alert.messageText = "Couldn’t update \(name)"
            alert.informativeText = "Worklings left your config file untouched.\n\n\(error)"
            alert.runModal()
        }
    }

    /// Removes every Worklings hook from both tools in one step — the clean
    /// pre-uninstall path. Each tool's config is backed up first, and a stale
    /// entry (from a moved/old adapter) is removed too, since disconnect matches
    /// our hooks by file name regardless of whether the path still resolves.
    @objc
    private func disconnectAllTools() {
        let tools: [(name: String, connector: ToolConnector)] = [
            ("Claude Code", claudeCodeConnector()),
            ("Codex", codexConnector())
        ]

        var removed: [String] = []
        var failures: [String] = []
        for tool in tools {
            switch tool.connector.connectionState() {
            case .notConnected:
                continue // nothing of ours to remove
            case .unknown:
                // Its config couldn't be read/parsed — we can't confirm it is
                // clean, so report it as a cleanup failure rather than a silent
                // "nothing found."
                failures.append("\(tool.name): its config couldn’t be read or parsed, so Worklings couldn’t check for or remove its hooks (\(tool.connector.configURL.path)).")
            case .live, .stale:
                do {
                    _ = try tool.connector.disconnect()
                    removed.append(tool.name)
                } catch {
                    failures.append("\(tool.name): \(error)")
                }
            }
        }

        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        if !failures.isEmpty {
            alert.messageText = "Some tools couldn’t be updated"
            alert.informativeText = (removed.isEmpty
                ? "Worklings left the config files untouched.\n\n"
                : "Disconnected \(removed.joined(separator: " and ")). The rest were left untouched:\n\n")
                + failures.joined(separator: "\n")
        } else if removed.isEmpty {
            alert.messageText = "Nothing to disconnect"
            alert.informativeText = "No Worklings hooks were found in Claude Code or Codex."
        } else {
            alert.messageText = "Disconnected \(removed.joined(separator: " and "))"
            alert.informativeText = "Worklings' hooks were removed. Each config was backed up first."
        }
        alert.runModal()
        updateToolConnectionItems()
    }

    /// Shows the informed-consent dialog before a tool's first connection and
    /// records the acknowledgement so it never appears again *for that tool*.
    /// Because each tool edits a different file, the disclosure is remembered per
    /// tool — connecting Codex after Claude still shows Codex's file. Returns true
    /// if the connection may proceed (already acknowledged, or the user chose
    /// Connect); false if the user cancelled.
    private func confirmToolConnectionConsent(toolName: String, configPath: String, consentKey: String) -> Bool {
        let defaultsKey = Self.toolConsentAcknowledgedKey(consentKey)
        if UserDefaults.standard.bool(forKey: defaultsKey) {
            return true
        }
        NSApp.activate(ignoringOtherApps: true)
        let alert = NSAlert()
        alert.messageText = "Connect \(toolName) to Worklings?"
        alert.informativeText = """
            Worklings will add its hooks to this file (backing it up first, and \
            preserving your existing settings and hooks):
            \(configPath)

            After that, \(toolName) tells Worklings only what happened — an activity \
            kind (like “task completed”), which tool it came from, and when. Never a \
            prompt, a diff, a file path, or any content. Everything stays on this Mac; \
            nothing is ever sent anywhere.

            You can undo this anytime: click the tool again to disconnect, or use \
            “Disconnect All Tools.” Do that before you delete Worklings so no hooks \
            are left behind.
            """
        alert.addButton(withTitle: "Connect")
        alert.addButton(withTitle: "Cancel")
        let proceed = alert.runModal() == .alertFirstButtonReturn
        if proceed {
            UserDefaults.standard.set(true, forKey: defaultsKey)
        }
        return proceed
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
