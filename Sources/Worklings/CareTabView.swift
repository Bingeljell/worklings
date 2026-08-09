import CompanionCore
import SwiftUI

/// Care, as a tab of the Character Screen.
///
/// This was the standalone care popover. It folded in here rather than being
/// duplicated: the design's standing rule is that care data must not be
/// triplicated across a popover, the roaming pet, and this screen, so there is
/// exactly one place to read needs and exactly one place to act on them. The
/// stats it used to carry live in the Character tab now, which is why only the
/// condition half made the journey.
struct CareTabView: View {
    @ObservedObject var session: PetSession

    private var state: PetState {
        session.state
    }

    private var status: PetCareStatus {
        PetCareStatus.make(state: state)
    }

    private var presentation: PetPresentation {
        PetPresentation.make(state: state, reaction: session.reaction)
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(presentation.thought ?? status.hoverSummary)
                .font(.subheadline)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            needs
            actions
            preferences
        }
        .frame(maxWidth: 420, alignment: .leading)
    }

    private var needs: some View {
        VStack(spacing: 10) {
            NeedProgressRow(
                label: "Fullness",
                value: state.needs.fullness,
                tint: .orange
            )
            NeedProgressRow(
                label: "Energy",
                value: state.needs.energy,
                tint: .blue
            )
            NeedProgressRow(
                label: "Happiness",
                value: state.needs.happiness,
                tint: .pink
            )
            NeedProgressRow(
                label: "Trust",
                value: state.needs.trust,
                tint: .purple
            )
        }
    }

    private var actions: some View {
        VStack(spacing: 9) {
            HStack(spacing: 9) {
                feedMenu
                playMenu
            }

            HStack(spacing: 9) {
                actionButton(
                    title: "Pet",
                    systemImage: "hand.raised.fill",
                    kind: .pet
                ) {
                    session.perform(.pet)
                }
                actionButton(
                    title: "Sleep",
                    systemImage: "moon.zzz.fill",
                    kind: .sleep
                ) {
                    session.perform(.sleep)
                }
            }

            focusSessionButton
            logWorkButton
        }
    }

    private var focusSessionButton: some View {
        let isActive = session.isFocusSessionActive

        return Button {
            session.toggleFocusSession()
        } label: {
            Label(
                isActive ? "End Focus Session" : "Start Focus Session",
                systemImage: isActive ? "stop.circle.fill" : "timer"
            )
            .frame(maxWidth: .infinity)
        }
        .buttonStyle(.bordered)
        .help(
            isActive
                ? "Wrap up this focus session."
                : "Tell \(state.name) you're settling in to work."
        )
        .frame(maxWidth: .infinity)
    }

    private var logWorkButton: some View {
        let availability = session.workLogAvailability()

        return Button {
            session.logWork()
        } label: {
            Label("Log Work", systemImage: "checkmark.circle.fill")
                .frame(maxWidth: .infinity)
        }
        .buttonStyle(.bordered)
        .disabled(!availability.isEnabled)
        .help(availability.explanation ?? "Tell \(state.name) about work you did.")
        .frame(maxWidth: .infinity)
    }

    private var feedMenu: some View {
        let availability = status.availability(for: .feed, state: state)

        return Menu {
            ForEach(PetFood.allCases, id: \.rawValue) { food in
                Button {
                    session.perform(.feed(food))
                } label: {
                    if food == state.preferences.favouriteFood {
                        Label(food.displayName, systemImage: "heart.fill")
                    } else {
                        Text(food.displayName)
                    }
                }
            }
        } label: {
            Label("Feed", systemImage: "fork.knife")
                .frame(maxWidth: .infinity)
        }
        .menuStyle(.borderlessButton)
        .disabled(!availability.isEnabled)
        .help(availability.explanation ?? "Choose something for \(state.name) to eat.")
        .frame(maxWidth: .infinity)
    }

    private var playMenu: some View {
        let availability = status.availability(for: .play, state: state)

        return Menu {
            ForEach(PetPlayActivity.allCases, id: \.rawValue) { activity in
                Button {
                    session.perform(.play(activity))
                } label: {
                    if activity == state.preferences.favouritePlayActivity {
                        Label(activity.displayName, systemImage: "heart.fill")
                    } else {
                        Text(activity.displayName)
                    }
                }
            }
        } label: {
            Label("Play", systemImage: "sparkles")
                .frame(maxWidth: .infinity)
        }
        .menuStyle(.borderlessButton)
        .disabled(!availability.isEnabled)
        .help(availability.explanation ?? "Choose an activity for \(state.name).")
        .frame(maxWidth: .infinity)
    }

    private func actionButton(
        title: String,
        systemImage: String,
        kind: PetCareActionKind,
        action: @escaping () -> Void
    ) -> some View {
        let availability = status.availability(for: kind, state: state)

        return Button(action: action) {
            Label(title, systemImage: systemImage)
                .frame(maxWidth: .infinity)
        }
        .buttonStyle(.bordered)
        .disabled(!availability.isEnabled)
        .help(availability.explanation ?? "\(title) \(state.name).")
        .frame(maxWidth: .infinity)
    }

    private var preferences: some View {
        HStack(spacing: 6) {
            Image(systemName: "heart.fill")
                .foregroundStyle(.pink)
            Text(state.preferences.favouriteFood.displayName)
            Text("·")
                .foregroundStyle(.secondary)
            Text(state.preferences.favouritePlayActivity.displayName)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
        .accessibilityLabel(
            "Favourite food: \(state.preferences.favouriteFood.displayName). "
            + "Favourite play: \(state.preferences.favouritePlayActivity.displayName)."
        )
    }
}

private struct NeedProgressRow: View {
    let label: String
    let value: Double
    let tint: Color

    var body: some View {
        HStack(spacing: 10) {
            Text(label)
                .font(.caption)
                .frame(width: 70, alignment: .leading)

            ProgressView(value: value, total: 100)
                .tint(tint)

            Text("\(Int(value.rounded()))")
                .font(.system(.caption, design: .monospaced))
                .frame(width: 26, alignment: .trailing)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(label)
        .accessibilityValue("\(Int(value.rounded())) out of 100")
    }
}
