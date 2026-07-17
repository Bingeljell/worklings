import CompanionCore
import SwiftUI

struct PlaceholderPetView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @ObservedObject var session: PetSession
    @State private var isBobbing = false

    private var presentation: PetPresentation {
        PetPresentation.make(state: session.state, reaction: session.reaction)
    }

    private var careStatus: PetCareStatus {
        session.careStatus
    }

    var body: some View {
        ZStack {
            Ellipse()
                .fill(.black.opacity(0.18))
                .frame(width: 92, height: 22)
                .blur(radius: 3)
                .offset(y: 58)

            VStack(spacing: -8) {
                ears

                ZStack {
                    RoundedRectangle(cornerRadius: 46, style: .continuous)
                        .fill(
                            LinearGradient(
                                colors: paletteColors,
                                startPoint: .topLeading,
                                endPoint: .bottomTrailing
                            )
                        )
                        .frame(width: 116, height: 108)
                        .shadow(color: .purple.opacity(0.35), radius: 10, y: 5)

                    face
                }
            }
            .offset(y: isBobbing ? -5 : 3)

            if let thought = presentation.thought {
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
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .contentShape(Rectangle())
        .onAppear {
            guard !reduceMotion else {
                return
            }

            withAnimation(.easeInOut(duration: 1.15).repeatForever(autoreverses: true)) {
                isBobbing = true
            }
        }
        .animation(.easeInOut(duration: 0.2), value: presentation)
        .accessibilityLabel("\(session.state.name), \(presentation.moodLabel.lowercased())")
        .accessibilityHint(careStatus.hoverSummary)
    }

    private var paletteColors: [Color] {
        switch presentation.palette {
        case .bright:
            [Color(red: 0.67, green: 0.45, blue: 1.00),
             Color(red: 0.38, green: 0.20, blue: 0.82)]
        case .calm:
            [Color(red: 0.54, green: 0.38, blue: 0.95),
             Color(red: 0.30, green: 0.20, blue: 0.72)]
        case .hungry:
            [Color(red: 0.98, green: 0.53, blue: 0.38),
             Color(red: 0.73, green: 0.25, blue: 0.32)]
        case .sleepy:
            [Color(red: 0.39, green: 0.54, blue: 0.88),
             Color(red: 0.22, green: 0.27, blue: 0.60)]
        case .sad:
            [Color(red: 0.40, green: 0.58, blue: 0.70),
             Color(red: 0.24, green: 0.34, blue: 0.48)]
        case .wary:
            [Color(red: 0.62, green: 0.52, blue: 0.66),
             Color(red: 0.35, green: 0.29, blue: 0.42)]
        }
    }

    private var ears: some View {
        HStack(spacing: 42) {
            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(Color(red: 0.42, green: 0.28, blue: 0.86))
                .frame(width: 28, height: 45)
                .rotationEffect(.degrees(-18))

            RoundedRectangle(cornerRadius: 7, style: .continuous)
                .fill(Color(red: 0.42, green: 0.28, blue: 0.86))
                .frame(width: 28, height: 45)
                .rotationEffect(.degrees(18))
        }
    }

    private var face: some View {
        VStack(spacing: 11) {
            eyes
            mouth
        }
        .offset(y: 5)
    }

    @ViewBuilder
    private var eyes: some View {
        if presentation.face == .sleepy {
            HStack(spacing: 31) {
                Capsule().frame(width: 15, height: 3)
                Capsule().frame(width: 15, height: 3)
            }
            .foregroundStyle(.white)
        } else {
            HStack(spacing: presentation.face == .wary ? 38 : 31) {
                eye
                eye
            }
        }
    }

    private var eye: some View {
        Circle()
            .fill(.white)
            .frame(width: 13, height: presentation.face == .sad ? 14 : 17)
            .overlay(Circle().fill(.black).frame(width: 6, height: 8))
    }

    @ViewBuilder
    private var mouth: some View {
        switch presentation.face {
        case .happy:
            Capsule()
                .fill(Color(red: 0.13, green: 0.08, blue: 0.25))
                .frame(width: 29, height: 13)
                .overlay(alignment: .bottom) {
                    Capsule()
                        .fill(Color(red: 1.00, green: 0.52, blue: 0.69))
                        .frame(width: 13, height: 5)
                        .padding(.bottom, 1)
                }
        case .hungry:
            Circle()
                .stroke(Color(red: 0.13, green: 0.08, blue: 0.25), lineWidth: 4)
                .frame(width: 16, height: 16)
        case .sad, .wary:
            Capsule()
                .fill(Color(red: 0.13, green: 0.08, blue: 0.25))
                .frame(width: 24, height: 5)
                .rotationEffect(.degrees(presentation.face == .wary ? -8 : 0))
        case .neutral, .sleepy:
            Capsule()
                .fill(Color(red: 0.13, green: 0.08, blue: 0.25))
                .frame(width: 21, height: 5)
        }
    }
}
