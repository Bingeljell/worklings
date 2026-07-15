import SwiftUI

struct PlaceholderPetView: View {
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var isBobbing = false

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
                                colors: [Color(red: 0.54, green: 0.38, blue: 0.95),
                                         Color(red: 0.30, green: 0.20, blue: 0.72)],
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
        .accessibilityLabel("Build Companion placeholder pet")
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
            HStack(spacing: 31) {
                Circle()
                    .fill(.white)
                    .frame(width: 13, height: 17)
                    .overlay(Circle().fill(.black).frame(width: 6, height: 8))

                Circle()
                    .fill(.white)
                    .frame(width: 13, height: 17)
                    .overlay(Circle().fill(.black).frame(width: 6, height: 8))
            }

            Capsule()
                .fill(Color(red: 0.13, green: 0.08, blue: 0.25))
                .frame(width: 29, height: 13)
                .overlay(alignment: .bottom) {
                    Capsule()
                        .fill(Color(red: 1.00, green: 0.52, blue: 0.69))
                        .frame(width: 13, height: 5)
                        .padding(.bottom, 1)
                }
        }
        .offset(y: 5)
    }
}
