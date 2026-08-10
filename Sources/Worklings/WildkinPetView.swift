import AppKit
import CompanionCore
import Foundation
import SwiftUI

struct WorklingPetView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @ObservedObject var session: PetSession
    @ObservedObject var motion: CompanionMotionState

    private var presentation: PetPresentation {
        PetPresentation.make(state: session.state, reaction: session.reaction)
    }

    private var careStatus: PetCareStatus {
        session.careStatus
    }

    var body: some View {
        ZStack {
            Group {
                Ellipse()
                    .fill(.black.opacity(0.18))
                    .frame(width: 92, height: 20)
                    .blur(radius: 3)
                    .offset(y: 62)

                TimelineView(
                    .periodic(from: .now, by: motion.isWalking ? 0.14 : 0.7)
                ) { context in
                    WorklingSprite(
                        family: session.state.family,
                        frame: spriteFrame(at: context.date)
                    )
                        .scaleEffect(
                            x: motion.facingDirection == .left ? 1 : -1,
                            y: 1
                        )
                }

                if let thought = presentation.thought, !motion.isTransitioning {
                    Text(thought)
                        .font(.system(size: 12, weight: .semibold, design: .rounded))
                        .foregroundStyle(Color(red: 0.13, green: 0.08, blue: 0.25))
                        .padding(.horizontal, 11)
                        .padding(.vertical, 7)
                        .background(.white.opacity(0.95), in: Capsule())
                        .shadow(color: .black.opacity(0.16), radius: 5, y: 2)
                        .offset(y: -79)
                        .transition(.scale.combined(with: .opacity))
                }

                if !motion.isTransitioning {
                    Text(session.state.name)
                        .font(.system(size: 11, weight: .bold, design: .rounded))
                        .foregroundStyle(.white)
                        .padding(.horizontal, 10)
                        .padding(.vertical, 4)
                        .background(.black.opacity(0.55), in: Capsule())
                        .offset(y: 74)
                        .accessibilityHidden(true)
                }
            }
            .opacity(motion.isPetVisible ? 1 : 0)

            if let transitionFrame = motion.transitionFrame {
                SmokeEffectSprite(frameIndex: transitionFrame.spriteIndex)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .animation(.easeInOut(duration: 0.2), value: presentation)
        .accessibilityLabel(
            "\(session.state.name), \(session.state.family.displayName), "
                + presentation.moodLabel.lowercased()
        )
        .accessibilityHint(careStatus.hoverSummary)
    }

    private func spriteFrame(at date: Date) -> WorklingSpriteFrame {
        if let reaction = session.reaction {
            return reaction == .tooTiredToPlay ? .sleepy : .caredFor
        }

        if motion.isWalking, !reduceMotion {
            let phase = Int(date.timeIntervalSinceReferenceDate / 0.14) % 4
            return switch phase {
            case 0: .walkContact
            case 1: .walkPassing
            case 2: .walkContactOpposite
            default: .walkPassingOpposite
            }
        }

        switch presentation.face {
        case .happy:
            return .happy
        case .neutral:
            guard !reduceMotion else {
                return .idle
            }
            let phase = Int(date.timeIntervalSinceReferenceDate / 0.7)
            return phase.isMultiple(of: 2) ? .idle : .idleBlink
        case .hungry:
            return .hungry
        case .sleepy:
            return .sleepy
        case .sad:
            return .sad
        case .wary:
            return .wary
        }
    }
}

struct SmokeEffectSprite: View {
    private static let resourceName = "worklings-smoke-effects"
    /// Same 4-column contract as the Workling sheets; cell size is read from the
    /// sheet so a higher-resolution re-bake needs no code change.
    private static let columnCount: CGFloat = 4

    let frameIndex: Int
    var size: CGFloat = 196

    private static let spriteSheet: CGImage? = {
        let resourceURL = Bundle.main.url(
            forResource: resourceName,
            withExtension: "png"
        ) ?? Bundle.module.url(
            forResource: resourceName,
            withExtension: "png"
        )

        guard let resourceURL,
              let sourceImage = NSImage(contentsOf: resourceURL) else {
            NSLog("Worklings could not load the smoke effects sprite sheet.")
            return nil
        }

        var proposedRect = NSRect(origin: .zero, size: sourceImage.size)
        return sourceImage.cgImage(
            forProposedRect: &proposedRect,
            context: nil,
            hints: nil
        )
    }()

    var body: some View {
        Group {
            if let frameImage {
                Image(decorative: frameImage, scale: 1, orientation: .up)
                    .resizable()
                    .interpolation(.none)
            }
        }
        .frame(width: size, height: size)
        .accessibilityHidden(true)
    }

    private var frameImage: CGImage? {
        guard let spriteSheet = Self.spriteSheet else { return nil }
        let cell = CGFloat(spriteSheet.width) / Self.columnCount
        return spriteSheet.cropping(
            to: CGRect(
                x: CGFloat(frameIndex % 4) * cell,
                y: CGFloat(frameIndex / 4) * cell,
                width: cell,
                height: cell
            )
        )
    }
}

struct WorklingSprite: View {
    /// Sheets are a fixed 4-column grid, so the cell size falls out of the sheet's
    /// own width rather than a baked constant. A 1024-wide sheet reads 256px cells,
    /// a 2048-wide one reads 512px — which is what lets a higher-resolution re-bake
    /// drop in one family at a time without touching this view.
    private static let columnCount: CGFloat = 4

    let family: PetFamily
    let frame: WorklingSpriteFrame
    /// Rendered edge length. Defaults to the desktop companion's size; the
    /// combat panel passes a smaller value.
    var size: CGFloat = 168

    private static let wildkinSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-wildkin-spritesheet",
        familyName: "Wildkin"
    )
    private static let elementalSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-elemental-spritesheet",
        familyName: "Elemental"
    )
    private static let relicbornSpriteSheet = loadSpriteSheet(
        resourceName: "worklings-relicborn-spritesheet",
        familyName: "Relicborn"
    )

    private static func loadSpriteSheet(
        resourceName: String,
        familyName: String
    ) -> CGImage? {
        let resourceURL = Bundle.main.url(
            forResource: resourceName,
            withExtension: "png"
        ) ?? Bundle.module.url(
            forResource: resourceName,
            withExtension: "png"
        )

        guard let resourceURL,
              let sourceImage = NSImage(contentsOf: resourceURL) else {
            NSLog("Worklings could not load the %@ sprite sheet.", familyName)
            return nil
        }

        var proposedRect = NSRect(origin: .zero, size: sourceImage.size)
        return sourceImage.cgImage(
            forProposedRect: &proposedRect,
            context: nil,
            hints: nil
        )
    }

    var body: some View {
        Group {
            if let frameImage {
                Image(decorative: frameImage, scale: 1, orientation: .up)
                    .resizable()
                    .interpolation(.none)
            } else {
                Image(systemName: "pawprint.fill")
                    .resizable()
                    .scaledToFit()
                    .foregroundStyle(.secondary)
                    .padding(48)
            }
        }
        .frame(width: size, height: size)
    }

    private var frameImage: CGImage? {
        guard let spriteSheet else { return nil }
        let cell = CGFloat(spriteSheet.width) / Self.columnCount
        return spriteSheet.cropping(
            to: CGRect(
                x: CGFloat(frame.column) * cell,
                y: CGFloat(frame.row) * cell,
                width: cell,
                height: cell
            )
        )
    }

    private var spriteSheet: CGImage? {
        switch family {
        case .wildkin: Self.wildkinSpriteSheet
        case .elemental: Self.elementalSpriteSheet
        case .relicborn: Self.relicbornSpriteSheet
        // Design-stage families: no art yet, so they fall through to the
        // placeholder glyph until their sheets are baked.
        case .glitchkin, .bloomglass: nil
        }
    }
}

enum WorklingSpriteFrame {
    case idle
    case idleBlink
    case walkContact
    case walkPassing
    case walkContactOpposite
    case walkPassingOpposite
    case happy
    case caredFor
    case hungry
    case sleepy
    case sad
    case wary
    case strike
    case hurt
    case lowHP
    case victory
    case downed
    case brace
    case signature

    var column: Int {
        switch self {
        case .idle, .walkContactOpposite, .hungry, .strike, .downed:
            return 0
        case .idleBlink, .walkPassingOpposite, .sleepy, .hurt, .brace:
            return 1
        case .walkContact, .happy, .sad, .lowHP, .signature:
            return 2
        case .walkPassing, .caredFor, .wary, .victory:
            return 3
        }
    }

    var row: Int {
        switch self {
        case .idle, .idleBlink, .walkContact, .walkPassing:
            return 0
        case .walkContactOpposite, .walkPassingOpposite, .happy, .caredFor:
            return 1
        case .hungry, .sleepy, .sad, .wary:
            return 2
        case .strike, .hurt, .lowHP, .victory:
            return 3
        case .downed, .brace, .signature:
            return 4
        }
    }
}
