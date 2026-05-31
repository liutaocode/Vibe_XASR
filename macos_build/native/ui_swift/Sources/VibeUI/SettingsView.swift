// ============================================================
//  Vibe XASR — Preferences window (5 tabs)
//  General / Dictation / Model / Permissions / About.
//
//  Every visible control is wired to the real host store via `SettingsBridge`
//  and localized live via `L10n`. (Previews fall back to the protocol's inert
//  default implementations + English.) No placeholder controls remain.
// ============================================================

import SwiftUI
import Combine

// MARK: - Host bridge

/// The host app (VibeIME) implements this so every *live* Settings control
/// reads/writes the real SettingsStore and applies its effect immediately. When
/// nil (e.g. SwiftUI previews) the controls fall back to in-memory
/// `SettingsState`, so the existing placeholder controls keep working unchanged.
///
/// Newer members carry default implementations (see the extension below) so
/// older hosts / previews still satisfy the protocol.
@MainActor
public protocol SettingsBridge: AnyObject {
    // Dock + hotkey (original live controls).
    var showDockIcon: Bool { get set }            // setter applies activation policy live
    var hotkeyKeyCode: Int { get }
    var hotkeyModifierOnly: Bool { get }
    func setHotkey(keyCode: Int, modifierOnly: Bool)

    // ----- Engine config: VAD + latency tier (rebuilds the engine live) -----
    var vadKind: String { get set }               // "fire" | "silero"
    var latencyTier: Int { get set }              // 160/480/960/1920
    /// True while the engine is rebuilding after a VAD/tier change (UI shows 切换中…).
    var engineSwapping: Bool { get }

    // ----- Dictation behaviour -----
    var insertMethod: String { get set }          // "paste" | "type"
    var padWriteEnabled: Bool { get set }
    var historyEnabled: Bool { get set }
    var launchAtLogin: Bool { get set }
    /// Leave each dictation result on the clipboard (issue #12). Default OFF.
    var clipboardOverwrite: Bool { get set }

    // ----- Sub-bridge for the Model tab -----
    var modelManager: ModelManagerBridge? { get }
    /// Apply a tier selection: triggers a download if needed and swaps the engine
    /// once available. Returns immediately; progress is observed via modelManager.
    func selectTier(_ tier: Int)

    // ----- Live permission reads (so the Permissions tab is real) -----
    func micGranted() -> Bool
    func accessibilityGranted() -> Bool
    func inputMonitoringGranted() -> Bool
    func openPermissionSettings(_ which: PermissionKind)

    // ----- Auto-update (Sparkle, implemented in the app target) -----
    /// User-initiated update check. The host drives Sparkle's updater UI
    /// (checks the appcast, downloads + verifies + installs if a newer build exists).
    func checkForUpdates()
}

/// Which permission a "open System Settings" button targets.
public enum PermissionKind: Sendable { case microphone, accessibility, inputMonitoring }

// MARK: - Model download source (line / 线路)

/// Which mirror a tier download pulls from. ModelScope is the default (faster in
/// CN); HuggingFace is the alternative. The concrete host downloader persists the
/// choice in UserDefaults (NOT SettingsStore) and builds per-source URLs.
public enum ModelDownloadSource: String, CaseIterable, Sendable {
    case modelScope   // default
    case huggingFace

    /// Short label shown in the segmented picker (brand names, not localized).
    public var label: String {
        switch self {
        case .modelScope:  return "ModelScope"
        case .huggingFace: return "HuggingFace"
        }
    }
}

/// UI-side seam for the download-line picker. The host's ModelDownloader (an
/// ObservableObject living in the app target) conforms; the Model tab downcasts
/// its `manager` to this to read/write the line. Defined here (not in Bridges.swift)
/// so VibeUI owns the contract the picker depends on.
@MainActor
public protocol ModelDownloadSourcing: AnyObject {
    var source: ModelDownloadSource { get set }
}

// Default implementations so previews / older hosts compile. These are inert.
public extension SettingsBridge {
    var vadKind: String { get { "fire" } set {} }
    var latencyTier: Int { get { 960 } set {} }
    var engineSwapping: Bool { false }
    var insertMethod: String { get { "paste" } set {} }
    var padWriteEnabled: Bool { get { false } set {} }
    var historyEnabled: Bool { get { true } set {} }
    var launchAtLogin: Bool { get { false } set {} }
    var clipboardOverwrite: Bool { get { false } set {} }
    var modelManager: ModelManagerBridge? { nil }
    func selectTier(_ tier: Int) {}
    func micGranted() -> Bool { true }
    func accessibilityGranted() -> Bool { false }
    func inputMonitoringGranted() -> Bool { false }
    func openPermissionSettings(_ which: PermissionKind) {}
}

// MARK: - Model-manager observation relay (issue #13 fix)

/// SwiftUI does NOT observe `@Published` changes through an existential like
/// `any ModelManagerBridge & ObservableObject` — only a CONCRETE `@ObservedObject`
/// / `@StateObject` triggers re-renders. So download progress published by the
/// host's `ModelDownloader` never reached the Model tab and the bar sat frozen.
///
/// This concrete relay subscribes to the erased manager's `objectWillChange` and
/// re-publishes it, so a `@StateObject ModelManagerRelay` in the Model tab DOES
/// re-render live as progress advances. It forwards the bridge calls through.
@MainActor
public final class ModelManagerRelay: ObservableObject {
    /// The erased concrete manager (host's ModelDownloader). May be nil in previews.
    public let manager: (any ModelManagerBridge & ObservableObject)?
    private var cancellable: AnyCancellable?

    public init(_ manager: (any ModelManagerBridge & ObservableObject)?) {
        self.manager = manager
        // Bridge the erased object's change publisher to ours. `eraseObjectWillChange`
        // hides the associated-type so we can subscribe through the existential.
        if let m = manager {
            cancellable = m.eraseObjectWillChange()
                .receive(on: RunLoop.main)
                .sink { [weak self] _ in self?.objectWillChange.send() }
        }
    }

    // Convenience pass-throughs the Model tab reads each render.
    func isTierDownloaded(_ tier: LatencyTier) -> Bool { manager?.isTierDownloaded(tier) ?? tier.isBundled }
    func downloadProgress(_ tier: LatencyTier) -> Double? { manager?.downloadProgress(tier) }
    func didTierFail(_ tier: LatencyTier) -> Bool { manager?.didTierFail(tier) ?? false }
    func startDownload(_ tier: LatencyTier) { manager?.startDownload(tier) }
    func cancelDownload(_ tier: LatencyTier) { manager?.cancelDownload(tier) }
    @discardableResult func deleteTier(_ tier: LatencyTier) -> Bool { manager?.deleteTier(tier) ?? false }
}

private extension ObservableObject {
    /// Type-erase `objectWillChange` so it can be subscribed through an existential.
    func eraseObjectWillChange() -> AnyPublisher<Void, Never> {
        objectWillChange.map { _ in () }.eraseToAnyPublisher()
    }
}

// MARK: - Backing state (transient UI only)

/// Minimal transient state. All *persisted* settings now live behind the bridge;
/// this only holds the hotkey display mirror used while a bridge is present, and
/// the in-memory fallbacks used by previews (no bridge).
@MainActor
public final class SettingsState: ObservableObject {
    // Hotkey mirror (display name + live values).
    @Published public var combo = "Right ⌘"
    @Published public var hotkeyKeyCode = 54
    @Published public var hotkeyModifierOnly = true

    // Preview-only fallbacks (used when bridge == nil).
    @Published public var showDockIcon = true
    @Published public var vad = "fire"
    @Published public var latency = 960
    @Published public var insert = "paste"
    @Published public var padWrite = false
    @Published public var history = true
    @Published public var launchAtLogin = false
    @Published public var clipOverwrite = false

    /// Host bridge; when set, controls read/write through it.
    public weak var bridge: SettingsBridge?

    public init() {}

    /// Seed the live values from the bridge (called by SettingsView at init).
    public func bind(to bridge: SettingsBridge) {
        self.bridge = bridge
        self.showDockIcon = bridge.showDockIcon
        self.hotkeyKeyCode = bridge.hotkeyKeyCode
        self.hotkeyModifierOnly = bridge.hotkeyModifierOnly
        self.combo = VibeKeycodes.name(bridge.hotkeyKeyCode)
        self.vad = bridge.vadKind
        self.latency = bridge.latencyTier
        self.insert = bridge.insertMethod
        self.padWrite = bridge.padWriteEnabled
        self.history = bridge.historyEnabled
        self.launchAtLogin = bridge.launchAtLogin
        self.clipOverwrite = bridge.clipboardOverwrite
    }

    // ---- write-throughs (bridge present) or local fallback (preview) -------

    public func applyDockIcon(_ on: Bool) {
        showDockIcon = on
        bridge?.showDockIcon = on
    }
    public func applyHotkey(keyCode: Int, modifierOnly: Bool) {
        hotkeyKeyCode = keyCode
        hotkeyModifierOnly = modifierOnly
        combo = VibeKeycodes.name(keyCode)
        bridge?.setHotkey(keyCode: keyCode, modifierOnly: modifierOnly)
    }
    public func applyVad(_ kind: String) {
        vad = kind
        bridge?.vadKind = kind
    }
    public func applyInsert(_ m: String) {
        insert = m
        bridge?.insertMethod = m
    }
    public func applyPadWrite(_ on: Bool) {
        padWrite = on
        bridge?.padWriteEnabled = on
    }
    public func applyHistory(_ on: Bool) {
        history = on
        bridge?.historyEnabled = on
    }
    public func applyLaunchAtLogin(_ on: Bool) {
        launchAtLogin = on
        bridge?.launchAtLogin = on
    }
    public func applyClipOverwrite(_ on: Bool) {
        clipOverwrite = on
        bridge?.clipboardOverwrite = on
    }
    public func applyTier(_ tier: Int) {
        latency = tier
        bridge?.selectTier(tier)
    }

    // ---- permission reads (delegate to the bridge; false if no bridge) ----
    public var engineSwapping: Bool { bridge?.engineSwapping ?? false }
    public func micGranted() -> Bool { bridge?.micGranted() ?? false }
    public func a11yGranted() -> Bool { bridge?.accessibilityGranted() ?? false }
    public func inputGranted() -> Bool { bridge?.inputMonitoringGranted() ?? false }
    public func openPermission(_ kind: PermissionKind) { bridge?.openPermissionSettings(kind) }
}

// MARK: - Atomic controls

/// `.sw` toggle: accent-gradient track when on, spring-sliding white knob.
struct VibeToggle: View {
    @Environment(\.colorScheme) private var scheme
    @Binding var on: Bool
    var body: some View {
        Button {
            withAnimation(Vibe.Motion.spring) { on.toggle() }
        } label: {
            ZStack(alignment: on ? .trailing : .leading) {
                Capsule()
                    .fill(on
                          ? AnyShapeStyle(Vibe.accentGradient)
                          : AnyShapeStyle(scheme == .light
                                          ? Color(hex: "#D8D8DE")
                                          : Vibe.Palette.surface2(scheme)))
                    .frame(width: 42, height: 25)
                Circle()
                    .fill(Color.white)
                    .frame(width: 20, height: 20)
                    .shadow(color: .black.opacity(0.3), radius: 1.5, y: 1)
                    .padding(.horizontal, 2.5)
            }
        }
        .buttonStyle(.plain)
    }
}

/// `.seg` segmented control: muted labels, raised "on" pill.
struct VibeSegmented: View {
    @Environment(\.colorScheme) private var scheme
    @Binding var value: String
    var options: [(String, String)]   // (value, label)
    var onChange: ((String) -> Void)? = nil
    var body: some View {
        HStack(spacing: 2) {
            ForEach(options, id: \.0) { opt in
                let isOn = value == opt.0
                Button {
                    value = opt.0
                    onChange?(opt.0)
                } label: {
                    Text(opt.1)
                        .font(Vibe.Fonts.ui(12))
                        .foregroundStyle(isOn
                                         ? Vibe.Palette.text(scheme)
                                         : Vibe.Palette.textMuted(scheme))
                        .padding(.vertical, 5).padding(.horizontal, 11)
                        .background(
                            RoundedRectangle(cornerRadius: 6, style: .continuous)
                                .fill(isOn ? Vibe.Palette.segOn(scheme) : .clear)
                                .shadow(color: isOn ? Vibe.Shadow.cardColor(scheme) : .clear,
                                        radius: isOn ? Vibe.Shadow.cardRadius : 0,
                                        y: isOn ? Vibe.Shadow.cardY : 0)
                        )
                }
                .buttonStyle(.plain)
            }
        }
        .padding(3)
        .background(
            RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                .fill(Vibe.Palette.surface2(scheme))
        )
    }
}

/// `.sel` dropdown styled like the mockup (surface-2 box + chevron).
struct VibeSelect: View {
    @Environment(\.colorScheme) private var scheme
    @Binding var value: String
    var options: [(String, String)]
    var onChange: ((String) -> Void)? = nil
    var body: some View {
        Menu {
            ForEach(options, id: \.0) { opt in
                Button(opt.1) { value = opt.0; onChange?(opt.0) }
            }
        } label: {
            HStack(spacing: 8) {
                Text(options.first(where: { $0.0 == value })?.1 ?? value)
                    .font(Vibe.Fonts.ui(12.5))
                    .foregroundStyle(Vibe.Palette.text(scheme))
                Text("⌄")
                    .font(.system(size: 13))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    .offset(y: -2)
            }
            .padding(.vertical, 7).padding(.leading, 12).padding(.trailing, 11)
            .background(
                RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                    .fill(Vibe.Palette.surface2(scheme))
            )
            .overlay(
                RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                    .strokeBorder(Vibe.Palette.hairline(scheme), lineWidth: 1)
            )
        }
        .menuStyle(.borderlessButton)
        .fixedSize()
    }
}

/// `.pill.ok / .pill.bad` permission status pill.
struct StatusPill: View {
    @ObservedObject private var l10n = L10n.shared
    var ok: Bool
    var body: some View {
        Text(ok ? l10n.t("perm.granted") : l10n.t("perm.denied"))
            .font(Vibe.Fonts.ui(12, weight: .semibold))
            .foregroundStyle(ok ? Vibe.Palette.success : Vibe.Palette.error)
            .padding(.vertical, 5).padding(.horizontal, 11)
            .background(
                Capsule().fill((ok ? Vibe.Palette.success : Vibe.Palette.error).opacity(0.16))
            )
    }
}

/// Solid accent button (`.m-btn`) with ghost / danger variants.
struct MButton: View {
    @Environment(\.colorScheme) private var scheme
    enum Kind { case solid, ghost, danger }
    var title: String
    var kind: Kind = .solid
    var action: () -> Void = {}
    var body: some View {
        Button(action: action) {
            Text(title)
                .font(Vibe.Fonts.ui(12.5))
                .foregroundStyle(foreground)
                .padding(.vertical, 7).padding(.horizontal, 14)
                .background(
                    RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                        .fill(background)
                )
                .overlay(
                    RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                        .strokeBorder(borderColor, lineWidth: kind == .solid ? 0 : 1)
                )
        }
        .buttonStyle(.plain)
    }
    private var foreground: Color {
        switch kind {
        case .solid:  return .white
        case .ghost:  return Vibe.Palette.text(scheme)
        case .danger: return Vibe.Palette.error
        }
    }
    private var background: Color {
        switch kind {
        case .solid:  return Vibe.Palette.accentA
        case .ghost:  return Vibe.Palette.surface2(scheme)
        case .danger: return .clear
        }
    }
    private var borderColor: Color {
        switch kind {
        case .solid:  return .clear
        case .ghost:  return Vibe.Palette.hairline(scheme)
        case .danger: return Vibe.Palette.hairline(scheme)
        }
    }
}

// MARK: - Row & Group containers

/// `.row`: title + optional help on the left, a control on the right.
struct SettingsRow<Control: View>: View {
    @Environment(\.colorScheme) private var scheme
    var title: String
    var help: String? = nil
    @ViewBuilder var control: () -> Control
    var body: some View {
        HStack(alignment: .center, spacing: 16) {
            VStack(alignment: .leading, spacing: 3) {
                Text(title)
                    .font(Vibe.Fonts.ui(13.5, weight: .medium))
                    .foregroundStyle(Vibe.Palette.text(scheme))
                if let help {
                    Text(help)
                        .font(Vibe.Fonts.ui(11.5))
                        .foregroundStyle(Vibe.Palette.textMuted(scheme))
                        .fixedSize(horizontal: false, vertical: true)
                }
            }
            Spacer(minLength: 8)
            control()
        }
        .padding(.vertical, 13).padding(.horizontal, 16)
        .background(Vibe.Palette.surface(scheme))
    }
}

/// `.grp`: an uppercase mono label over a hairline-separated card of rows.
struct SettingsGroup<Content: View>: View {
    @Environment(\.colorScheme) private var scheme
    var label: String? = nil
    @ViewBuilder var content: () -> Content
    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            if let label {
                Text(label)
                    .font(Vibe.Fonts.mono(11))
                    .tracking(1.1)
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    .textCase(.uppercase)
                    .padding(.leading, 2).padding(.bottom, 10)
            }
            VStack(spacing: 1) {
                content()
            }
            .background(Vibe.Palette.hairline(scheme)) // 1px gaps show as hairlines
            .clipShape(RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous)
                    .strokeBorder(Vibe.Palette.hairline(scheme), lineWidth: 1)
            )
        }
        .padding(.bottom, 26)
    }
}

// MARK: - Tabs

private struct GeneralTab: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    var body: some View {
        SettingsGroup(label: l10n.t("grp.general")) {
            SettingsRow(title: l10n.t("gen.dock"), help: l10n.t("gen.dock.help")) {
                VibeToggle(on: Binding(get: { s.showDockIcon },
                                       set: { s.applyDockIcon($0) }))
            }
            SettingsRow(title: l10n.t("gen.launchAtLogin"), help: l10n.t("gen.launchAtLogin.help")) {
                VibeToggle(on: Binding(get: { s.launchAtLogin },
                                       set: { s.applyLaunchAtLogin($0) }))
            }
            SettingsRow(title: l10n.t("gen.lang"), help: l10n.t("gen.lang.help")) {
                // Real, live UI-language picker. Auto label is itself localized.
                VibeSelect(
                    value: Binding(get: { l10n.lang.rawValue },
                                   set: { l10n.lang = Lang(rawValue: $0) ?? .auto }),
                    options: Lang.allCases.map {
                        ($0.rawValue, $0 == .auto ? l10n.autoLabel() : $0.display)
                    })
            }
        }
    }
}

/// (听写模式 / Dictation mode) The three insert behaviours rendered as a
/// radio-style vertical list inside the Dictation group. Each row carries a long
/// description, so a segmented control won't fit. The whole row is the hit target;
/// selecting writes through `s.applyInsert(value)`.
private struct DictationModeList: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme

    /// (value, titleKey, descKey) for the three modes.
    private var modes: [(String, String, String)] {
        [("paste",  "dict.mode.paste.title",  "dict.mode.paste.desc"),
         ("type",   "dict.mode.type.title",   "dict.mode.type.desc"),
         ("oncall", "dict.mode.oncall.title", "dict.mode.oncall.desc")]
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Section caption row (matches a SettingsRow's left column styling).
            VStack(alignment: .leading, spacing: 3) {
                Text(l10n.t("dict.mode"))
                    .font(Vibe.Fonts.ui(13.5, weight: .medium))
                    .foregroundStyle(Vibe.Palette.text(scheme))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.top, 13).padding(.bottom, 6).padding(.horizontal, 16)

            VStack(spacing: 8) {
                ForEach(modes, id: \.0) { mode in
                    DictationModeRow(
                        title: l10n.t(mode.1),
                        desc: l10n.t(mode.2),
                        selected: s.insert == mode.0
                    ) { s.applyInsert(mode.0) }
                }
            }
            .padding(.horizontal, 16).padding(.bottom, 13)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Vibe.Palette.surface(scheme))
    }
}

/// One tappable dictation-mode option: title + long description, with a selected
/// ring + checkmark. The whole row is the hit target.
private struct DictationModeRow: View {
    @Environment(\.colorScheme) private var scheme
    var title: String
    var desc: String
    var selected: Bool
    var action: () -> Void
    var body: some View {
        Button(action: action) {
            HStack(alignment: .top, spacing: 11) {
                // Radio dot.
                ZStack {
                    Circle()
                        .strokeBorder(selected ? Vibe.Palette.accentA
                                               : Vibe.Palette.hairline(scheme),
                                      lineWidth: selected ? 5 : 1.5)
                        .frame(width: 18, height: 18)
                    if selected {
                        Image(systemName: "checkmark")
                            .font(.system(size: 8, weight: .bold))
                            .foregroundStyle(.white)
                    }
                }
                .padding(.top, 1)
                VStack(alignment: .leading, spacing: 3) {
                    Text(title)
                        .font(Vibe.Fonts.ui(13, weight: .semibold))
                        .foregroundStyle(Vibe.Palette.text(scheme))
                    Text(desc)
                        .font(Vibe.Fonts.ui(11.5))
                        .foregroundStyle(Vibe.Palette.textMuted(scheme))
                        .multilineTextAlignment(.leading)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                }
                Spacer(minLength: 0)
            }
            .padding(.vertical, 11).padding(.horizontal, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(selected ? Vibe.Palette.accentSoft(scheme)
                                   : Vibe.Palette.surface2(scheme))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .strokeBorder(selected ? Vibe.Palette.accentA
                                           : Vibe.Palette.hairline(scheme),
                                  lineWidth: selected ? 1.5 : 1)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}

private struct DictationTab: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme
    var body: some View {
        SettingsGroup(label: l10n.t("grp.dictation")) {
            SettingsRow(title: l10n.t("dict.hotkey"), help: l10n.t("dict.hotkey.help")) {
                HotkeyRecorder(
                    keyCode: Binding(get: { s.hotkeyKeyCode }, set: { s.hotkeyKeyCode = $0 }),
                    isModifier: Binding(get: { s.hotkeyModifierOnly }, set: { s.hotkeyModifierOnly = $0 })
                ) { code, mod in
                    s.applyHotkey(keyCode: code, modifierOnly: mod)
                }
            }
            // (听写模式 / Dictation mode) A radio-style vertical list — each option
            // carries a long description, so a tiny segmented control won't fit.
            DictationModeList(s: s, l10n: l10n)
            SettingsRow(title: l10n.t("dict.clipOverwrite"), help: l10n.t("dict.clipOverwrite.help")) {
                VibeToggle(on: Binding(get: { s.clipOverwrite }, set: { s.applyClipOverwrite($0) }))
            }
            SettingsRow(title: l10n.t("dict.history"), help: l10n.t("dict.history.help")) {
                VibeToggle(on: Binding(get: { s.history }, set: { s.applyHistory($0) }))
            }
        }
    }
}

// ---- Model tab (VAD + latency tiers + model management) -------------------

private struct ModelTab: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme
    /// Concrete relay around the erased model manager so SwiftUI re-renders live on
    /// the host's @Published download progress (issue #13). Owned here via
    /// @StateObject so its Combine subscription persists across re-renders.
    @StateObject var relay: ModelManagerRelay
    /// Re-poll the bridge's `engineSwapping` while the tab is visible so the
    /// banner appears/clears promptly (the engine swaps off the download path, so
    /// the manager's own progress updates don't always cover it).
    private let swapPoll = Timer.publish(every: 0.4, on: .main, in: .common).autoconnect()
    @State private var swapping = false

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // (5) Prominent inline "switching" banner at the TOP of the tab, near
            // the tier selector — replaces the easy-to-miss inline pill.
            if swapping { SwitchingBanner(l10n: l10n) }

            // (1)(2) X-ASR streaming ASR models — the PROMINENT, top section.
            SettingsGroup(label: l10n.t("grp.xasr")) {
                // Headline crediting the core ASR model this app is built around.
                ModelHeadline(l10n: l10n)
                // (issue #4) No more source chooser — HuggingFace is the only line.
                // Keep a subtle informational line so users know where models come from.
                ModelSourceLine(l10n: l10n)
                // Latency tier: a 2x2 of selectable scenario cards.
                tierPicker
                SettingsRow(title: l10n.t("model.aslang")) {
                    Text(l10n.t("model.aslang.zhen"))
                        .font(Vibe.Fonts.ui(12.5))
                        .foregroundStyle(Vibe.Palette.textMuted(scheme))
                }
            }

            // Per-tier download / use / delete management (still part of the
            // prominent X-ASR section, grouped under "MODEL MANAGEMENT").
            SettingsGroup(label: l10n.t("grp.models")) {
                ForEach(LatencyTier.allCases) { tier in
                    TierModelRow(tier: tier, l10n: l10n, relay: relay,
                                 isActive: s.latency == tier.rawValue) {
                        s.applyTier(tier.rawValue)
                    }
                }
            }

            // (2) VAD — moved BELOW and visually de-emphasized (smaller, muted).
            VadSection(s: s, l10n: l10n)
        }
        .onAppear {
            swapping = s.engineSwapping
        }
        .onReceive(swapPoll) { _ in
            let now = s.engineSwapping
            if now != swapping { withAnimation(Vibe.Motion.easeOut) { swapping = now } }
        }
    }

    private var tierPicker: some View {
        VStack(alignment: .leading, spacing: 0) {
            SettingsRow(title: l10n.t("model.tier"), help: l10n.t("model.tier.help")) {
                EmptyView()
            }
            // 2x2 scenario grid (each card switches the tier).
            let cols = [GridItem(.flexible(), spacing: 8), GridItem(.flexible(), spacing: 8)]
            LazyVGrid(columns: cols, spacing: 8) {
                ForEach(LatencyTier.allCases) { tier in
                    TierCard(tier: tier, l10n: l10n,
                             selected: s.latency == tier.rawValue,
                             downloaded: relay.isTierDownloaded(tier)) {
                        s.applyTier(tier.rawValue)
                    }
                }
            }
            .padding(.horizontal, 16).padding(.bottom, 14)
            .background(Vibe.Palette.surface(scheme))
        }
    }
}

/// (issue #4) Subtle informational line showing where models are downloaded from
/// (HuggingFace) — replaces the removed source segmented picker. Tappable link.
private struct ModelSourceLine: View {
    @Environment(\.colorScheme) private var scheme
    @ObservedObject var l10n: L10n
    private let url = URL(string: "https://huggingface.co/GilgameshWind/X-ASR-zh-en")!
    var body: some View {
        SettingsRow(title: l10n.t("model.source")) {
            Link(destination: url) {
                Text(l10n.t("model.source.value"))
                    .font(Vibe.Fonts.mono(11))
                    .foregroundStyle(Vibe.Palette.accentB)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
        }
    }
}

/// A short prominent line crediting the X-ASR zh-en streaming model — sits atop
/// the Model tab so the core ASR contribution reads first.
private struct ModelHeadline: View {
    @Environment(\.colorScheme) private var scheme
    @ObservedObject var l10n: L10n
    var body: some View {
        HStack(alignment: .top, spacing: 11) {
            Text(l10n.t("model.headline.title"))
                .font(Vibe.Fonts.ui(14, weight: .semibold))
                .foregroundStyle(Vibe.accentGradient)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            Text(l10n.t("model.headline.repo"))
                .font(Vibe.Fonts.mono(10.5))
                .foregroundStyle(Vibe.Palette.accentB)
                .padding(.vertical, 3).padding(.horizontal, 8)
                .background(Capsule().fill(Vibe.Palette.accentSoft(scheme)))
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 12).padding(.horizontal, 16)
        .background(Vibe.Palette.surface(scheme))
    }
}

/// The VAD models, rendered below the ASR section and visually de-emphasized
/// (smaller group label + muted help) so it "doesn't draw much attention".
private struct VadSection: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme
    var body: some View {
        SettingsGroup(label: l10n.t("grp.vad")) {
            SettingsRow(title: l10n.t("model.vad"), help: l10n.t("model.vad.help")) {
                VibeSelect(value: Binding(get: { s.vad }, set: { _ in }),
                           options: [("fire", l10n.t("model.vad.fire")),
                                     ("silero", l10n.t("model.vad.silero"))],
                           onChange: { s.applyVad($0) })
            }
        }
        .opacity(0.82)   // subtly recessed relative to the prominent X-ASR section
    }
}

/// A selectable latency-tier scenario card.
private struct TierCard: View {
    @Environment(\.colorScheme) private var scheme
    var tier: LatencyTier
    @ObservedObject var l10n: L10n
    var selected: Bool
    var downloaded: Bool
    var action: () -> Void
    var body: some View {
        Button(action: action) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 6) {
                    Text(l10n.t(tier.nameKey))
                        .font(Vibe.Fonts.ui(13, weight: .semibold))
                        .foregroundStyle(Vibe.Palette.text(scheme))
                    if tier.isBundled {
                        Tag(text: l10n.t("model.bundled"))
                    } else if !downloaded {
                        Tag(text: "↓")
                    }
                    Spacer()
                    if selected {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.system(size: 13))
                            .foregroundStyle(Vibe.Palette.accentA)
                    }
                }
                Text(l10n.t(tier.sceneKey))
                    .font(Vibe.Fonts.ui(11))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    .multilineTextAlignment(.leading)
                    .fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
            .padding(.vertical, 10).padding(.horizontal, 12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .fill(selected ? Vibe.Palette.accentSoft(scheme)
                                   : Vibe.Palette.surface2(scheme))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 9, style: .continuous)
                    .strokeBorder(selected ? Vibe.Palette.accentA
                                           : Vibe.Palette.hairline(scheme),
                                  lineWidth: selected ? 1.5 : 1)
            )
        }
        .buttonStyle(.plain)
    }
}

private struct Tag: View {
    @Environment(\.colorScheme) private var scheme
    var text: String
    var body: some View {
        Text(text)
            .font(Vibe.Fonts.mono(9.5))
            .foregroundStyle(Vibe.Palette.textMuted(scheme))
            .padding(.vertical, 1).padding(.horizontal, 5)
            .background(RoundedRectangle(cornerRadius: 4, style: .continuous)
                .fill(Vibe.Palette.surface(scheme)))
    }
}

/// (5) Prominent switching banner — accent-tinted, full-width, pinned to the top
/// of the Model tab so "切换中…" is impossible to miss (the old inline pill next
/// to the VAD select was easy to overlook).
private struct SwitchingBanner: View {
    @Environment(\.colorScheme) private var scheme
    @ObservedObject var l10n: L10n
    var body: some View {
        HStack(spacing: 9) {
            ProgressView().controlSize(.small)
            Text(l10n.t("model.switching.banner"))
                .font(Vibe.Fonts.ui(13, weight: .semibold))
                .foregroundStyle(Vibe.Palette.accentA)
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.vertical, 11).padding(.horizontal, 14)
        .background(
            RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous)
                .fill(Vibe.Palette.accentSoft(scheme))
        )
        .overlay(
            RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous)
                .strokeBorder(Vibe.Palette.accentA.opacity(0.45), lineWidth: 1)
        )
        .padding(.bottom, 16)
        .transition(.move(edge: .top).combined(with: .opacity))
    }
}

/// One row in the model-management list (per tier): name + state + action.
private struct TierModelRow: View {
    @Environment(\.colorScheme) private var scheme
    var tier: LatencyTier
    @ObservedObject var l10n: L10n
    @ObservedObject var relay: ModelManagerRelay
    var isActive: Bool
    var onUse: () -> Void

    var body: some View {
        let progress = relay.downloadProgress(tier)
        let downloaded = relay.isTierDownloaded(tier)
        let failed = relay.didTierFail(tier)
        return HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 4) {
                HStack(spacing: 8) {
                    Text(String(format: l10n.t("model.tierRow"), l10n.t(tier.nameKey)))
                        .font(Vibe.Fonts.ui(13.5, weight: .medium))
                        .foregroundStyle(Vibe.Palette.text(scheme))
                    if isActive {
                        Text(l10n.t("model.active"))
                            .font(Vibe.Fonts.mono(10))
                            .foregroundStyle(Vibe.Palette.accentB)
                            .padding(.vertical, 2).padding(.horizontal, 6)
                            .background(RoundedRectangle(cornerRadius: 5)
                                .fill(Vibe.Palette.accentSoft(scheme)))
                    }
                }
                if let p = progress {
                    HStack(spacing: 9) {
                        ProgressBar(fraction: p).frame(maxWidth: 180)
                        // Before the first byte (p==0) show "Starting download…" so the
                        // click gives IMMEDIATE visible feedback (issue #13), then the
                        // determinate "Downloading N%" once bytes arrive.
                        Text(p > 0 ? String(format: l10n.t("model.downloading"), Int(p * 100))
                                   : l10n.t("model.dl.starting"))
                            .font(Vibe.Fonts.mono(11))
                            .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    }
                } else {
                    HStack(spacing: 4) {
                        Text(tier.approxSize + " · ")
                            .foregroundStyle(Vibe.Palette.textMuted(scheme))
                        if failed {
                            Text(l10n.t("model.dl.failed"))
                                .foregroundStyle(Vibe.Palette.error)
                        } else if tier.isBundled {
                            Text(l10n.t("model.bundled")).foregroundStyle(Vibe.Palette.success)
                        } else {
                            Text(downloaded ? l10n.t("model.downloaded") : l10n.t("model.notDownloaded"))
                                .foregroundStyle(downloaded ? Vibe.Palette.success
                                                            : Vibe.Palette.textMuted(scheme))
                        }
                    }
                    .font(Vibe.Fonts.mono(11.5))
                }
            }
            Spacer(minLength: 8)
            action(progress: progress, downloaded: downloaded, failed: failed)
        }
        .padding(.vertical, 13).padding(.horizontal, 16)
        .background(Vibe.Palette.surface(scheme))
    }

    @ViewBuilder
    private func action(progress: Double?, downloaded: Bool, failed: Bool) -> some View {
        if progress != nil {
            MButton(title: l10n.t("cancel"), kind: .ghost) { relay.cancelDownload(tier) }
        } else if failed {
            MButton(title: l10n.t("download"), kind: .solid) { relay.startDownload(tier) }
        } else if downloaded {
            HStack(spacing: 8) {
                if !isActive {
                    MButton(title: l10n.t("model.use"), kind: .solid) { onUse() }
                }
                // The bundled tier can't be deleted (read-only in the bundle).
                if !tier.isBundled {
                    MButton(title: l10n.t("delete"), kind: .danger) {
                        _ = relay.deleteTier(tier)
                    }
                }
            }
        } else {
            MButton(title: l10n.t("download"), kind: .solid) { relay.startDownload(tier) }
        }
    }
}

/// `.bar` / `.bar i` gradient progress bar.
struct ProgressBar: View {
    @Environment(\.colorScheme) private var scheme
    var fraction: Double
    var body: some View {
        GeometryReader { geo in
            ZStack(alignment: .leading) {
                Capsule().fill(Vibe.Palette.surface2(scheme))
                Capsule().fill(Vibe.accentGradient)
                    .frame(width: max(0, min(1, fraction)) * geo.size.width)
            }
        }
        .frame(height: 5)
    }
}

// ---- Permissions tab (live) ----------------------------------------------

private struct PermissionsTab: View {
    @ObservedObject var s: SettingsState
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme
    @State private var mic = false
    @State private var a11y = false
    @State private var input = false
    @State private var checking = false
    /// Re-poll while the window is visible.
    private let poll = Timer.publish(every: 1.0, on: .main, in: .common).autoconnect()

    private var allOk: Bool { mic && a11y && input }

    var body: some View {
        SettingsGroup(label: l10n.t("grp.permissions")) {
            Banner(ok: allOk, l10n: l10n)
            PermRow(l10n: l10n, key: "perm.mic", granted: mic) { s.openPermission(.microphone) }
            PermRow(l10n: l10n, key: "perm.a11y", granted: a11y) { s.openPermission(.accessibility) }
            PermRow(l10n: l10n, key: "perm.input", granted: input) { s.openPermission(.inputMonitoring) }
            HStack {
                Spacer()
                MButton(title: checking ? l10n.t("perm.checking") : l10n.t("perm.recheck"),
                        kind: .ghost) {
                    checking = true
                    refresh()
                    DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { checking = false }
                }
            }
            .padding(.vertical, 12).padding(.horizontal, 16)
            .background(Vibe.Palette.surface(scheme))
        }
        .onAppear(perform: refresh)
        .onReceive(poll) { _ in refresh() }
    }

    private func refresh() {
        mic = s.micGranted()
        a11y = s.a11yGranted()
        input = s.inputGranted()
    }
}

/// A single permission row (status pill + "open settings" when not granted).
/// Takes a plain `onOpen` closure so its body never reaches through the parent's
/// @ObservedObject — which otherwise confuses Swift's closure type inference.
private struct PermRow: View {
    @ObservedObject var l10n: L10n
    var key: String
    var granted: Bool
    var onOpen: () -> Void
    var body: some View {
        SettingsRow(title: l10n.t(key), help: l10n.t(key + ".help")) {
            HStack(spacing: 10) {
                StatusPill(ok: granted)
                if !granted {
                    MButton(title: l10n.t("perm.openSettings"), kind: .solid, action: onOpen)
                }
            }
        }
    }
}

/// `.banner.warn / .banner.ok` advisory banner.
private struct Banner: View {
    var ok: Bool
    @ObservedObject var l10n: L10n
    var body: some View {
        let tint = ok ? Vibe.Palette.success : Vibe.Palette.warn
        let text = ok ? l10n.t("perm.banner.ok") : l10n.t("perm.banner.warn")
        return Text(text)
            .font(Vibe.Fonts.ui(12.5))
            .foregroundStyle(tint)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 11).padding(.horizontal, 14)
            .background(tint.opacity(0.13))
            .overlay(
                Rectangle().fill(tint.opacity(0.30)).frame(height: 1),
                alignment: .bottom)
    }
}

/// (issue #8) The "Records / 记录" sidebar tab. Renders the host-supplied
/// `records` view (an embedded HistoryView) when present; otherwise a centered
/// "No records yet" hint (previews pass nil).
private struct RecordsTab: View {
    @ObservedObject var l10n: L10n
    @Environment(\.colorScheme) private var scheme
    var records: AnyView?
    var body: some View {
        if let records {
            records.frame(maxWidth: .infinity, maxHeight: .infinity)
        } else {
            VStack(spacing: 10) {
                Spacer()
                Text("📋").font(.system(size: 40)).opacity(0.5)
                Text(l10n.t("records.empty"))
                    .font(Vibe.Fonts.ui(13))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
                Spacer()
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }
}

private struct AboutTab: View {
    @ObservedObject var l10n: L10n
    weak var bridge: SettingsBridge?
    @Environment(\.colorScheme) private var scheme
    /// Secondary acknowledgments — the supporting OSS stack, shown small/below.
    private let secondaryCredits = ["sherpa-onnx", "FireRedVAD", "onnxruntime",
                                    "kaldi-native-fbank", "silero-vad", "kissfft"]
    var body: some View {
        SettingsGroup {
            VStack(spacing: 12) {
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .fill(Vibe.accentGradient)
                    .frame(width: 64, height: 64)
                    .overlay(LogoBars(heights: [10, 22, 30, 18, 12], barW: 4, gap: 3))
                    .shadow(color: Vibe.Palette.accentA.opacity(0.45), radius: 15, y: 10)
                Text(l10n.t("app.name")).font(Vibe.Fonts.ui(22, weight: .bold))
                    .foregroundStyle(Vibe.Palette.text(scheme))
                Text(String(format: l10n.t("about.version"),
                            Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "1.1.1"))
                    .font(Vibe.Fonts.mono(11.5))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))

                // Sparkle update check — drives the host's updater (appcast on GitHub Pages).
                Button { bridge?.checkForUpdates() } label: {
                    HStack(spacing: 6) {
                        Image(systemName: "arrow.triangle.2.circlepath")
                        Text(l10n.t("about.checkUpdate"))
                    }
                    .font(Vibe.Fonts.ui(12, weight: .medium))
                    .foregroundStyle(Vibe.Palette.accentB)
                    .padding(.horizontal, 14).padding(.vertical, 7)
                    .background(
                        Capsule().fill(Vibe.Palette.accentB.opacity(0.12))
                    )
                }
                .buttonStyle(.plain)
                .padding(.top, 4)

                // (About) BIG, prominent X-ASR credit — the core ASR model this
                // whole app is built around.
                XASRCredit(l10n: l10n)
                    .padding(.top, 22)

                // Secondary acknowledgments — smaller, muted, below the X-ASR card.
                VStack(spacing: 9) {
                    Text(l10n.t("about.credits"))
                        .font(Vibe.Fonts.mono(10.5)).tracking(1.1)
                        .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    // Wrapping flow of small chips (6 items won't fit one row).
                    FlowChips(items: secondaryCredits)
                }
                .padding(.top, 22)

                Text(l10n.t("about.local"))
                    .font(Vibe.Fonts.mono(11))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    .padding(.top, 18)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 36).padding(.horizontal, 24)
            .background(Vibe.Palette.surface(scheme))
        }
    }
}

/// The headline X-ASR acknowledgment card: large title + one-line description +
/// the HF repo, on an accent-soft surface so it visually leads the About tab.
private struct XASRCredit: View {
    @Environment(\.colorScheme) private var scheme
    @ObservedObject var l10n: L10n
    private let ghURL = URL(string: "https://github.com/Gilgamesh-J/X-ASR")!
    private let hfURL = URL(string: "https://huggingface.co/GilgameshWind/X-ASR-zh-en")!
    var body: some View {
        VStack(spacing: 7) {
            // (issue #7) The X-ASR title opens the GitHub repo.
            Link(destination: ghURL) {
                Text(l10n.t("about.xasr.title"))
                    .font(Vibe.Fonts.ui(19, weight: .bold))
                    .foregroundStyle(Vibe.accentGradient)
                    .multilineTextAlignment(.center)
            }
            Text(l10n.t("about.xasr.desc"))
                .font(Vibe.Fonts.ui(12.5))
                .foregroundStyle(Vibe.Palette.text(scheme))
                .multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
            HStack(spacing: 12) {
                Link(destination: ghURL) {
                    Text("github.com/Gilgamesh-J/X-ASR")
                        .font(Vibe.Fonts.mono(11))
                        .foregroundStyle(Vibe.Palette.accentB)
                }
                Link(destination: hfURL) {
                    Text(l10n.t("about.xasr.repo"))
                        .font(Vibe.Fonts.mono(11))
                        .foregroundStyle(Vibe.Palette.accentB)
                }
            }
            .padding(.top, 2)
        }
        .frame(maxWidth: .infinity)
        .padding(.vertical, 18).padding(.horizontal, 18)
        .background(
            RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous)
                .fill(Vibe.Palette.accentSoft(scheme))
        )
        .overlay(
            RoundedRectangle(cornerRadius: Vibe.Radius.card, style: .continuous)
                .strokeBorder(Vibe.Palette.accentA.opacity(0.40), lineWidth: 1)
        )
    }
}

/// A simple wrapping chip flow (small, muted) for the secondary credits. Each chip
/// opens the project's repository (issue #7).
private struct FlowChips: View {
    @Environment(\.colorScheme) private var scheme
    var items: [String]

    /// Map each acknowledged project to its upstream repository URL.
    private func url(for name: String) -> URL? {
        let map: [String: String] = [
            "sherpa-onnx": "https://github.com/k2-fsa/sherpa-onnx",
            "FireRedVAD": "https://github.com/FireRedTeam/FireRedVAD",
            "onnxruntime": "https://github.com/microsoft/onnxruntime",
            "kaldi-native-fbank": "https://github.com/csukuangfj/kaldi-native-fbank",
            "silero-vad": "https://github.com/snakers4/silero-vad",
            "kissfft": "https://github.com/mborgerding/kissfft",
        ]
        return map[name].flatMap(URL.init(string:))
    }

    var body: some View {
        // A fixed 3-column grid wraps cleanly for the ~6 secondary credits and
        // avoids a custom Layout (keeps macOS 13 compatibility).
        let cols = [GridItem(.adaptive(minimum: 96, maximum: 160), spacing: 7)]
        LazyVGrid(columns: cols, alignment: .center, spacing: 7) {
            ForEach(items, id: \.self) { c in
                chip(c)
            }
        }
        .frame(maxWidth: 360)
    }

    @ViewBuilder
    private func chip(_ c: String) -> some View {
        let label = Text(c)
            .font(Vibe.Fonts.ui(11))
            .foregroundStyle(Vibe.Palette.textMuted(scheme))
            .lineLimit(1)
            .padding(.vertical, 5).padding(.horizontal, 10)
            .frame(maxWidth: .infinity)
            .background(Capsule().fill(Vibe.Palette.surface2(scheme)))
            .overlay(Capsule().strokeBorder(Vibe.Palette.hairline(scheme), lineWidth: 1))
        if let u = url(for: c) {
            Link(destination: u) { label }
        } else {
            label
        }
    }
}

/// The white equalizer bars used in the logo tiles.
struct LogoBars: View {
    var heights: [CGFloat]
    var barW: CGFloat
    var gap: CGFloat
    var body: some View {
        HStack(alignment: .center, spacing: gap) {
            ForEach(heights.indices, id: \.self) { i in
                RoundedRectangle(cornerRadius: barW / 2, style: .continuous)
                    .fill(Color.white)
                    .frame(width: barW, height: heights[i])
            }
        }
    }
}

// MARK: - Preferences window shell

public struct SettingsView: View {
    @StateObject private var s = SettingsState()
    @ObservedObject private var l10n = L10n.shared
    @State private var tab = "general"
    @Environment(\.colorScheme) private var scheme

    /// Host bridge for the live (store-backed) controls; nil in previews.
    private let bridge: SettingsBridge?
    /// Concrete model manager (observable) for the Model tab, if the host gave one.
    private let manager: (any ModelManagerBridge & ObservableObject)?
    /// (issue #8) Content for the always-visible "Records" sidebar tab — the host
    /// passes an embedded HistoryView here. Nil in previews → an empty hint shows.
    private let records: AnyView?

    private var tabs: [(id: String, label: String, icon: String)] {
        [("general", l10n.t("tab.general"), "⚙"),
         ("dictation", l10n.t("tab.dictation"), "🎙"),
         ("model", l10n.t("tab.model"), "🧠"),
         ("records", l10n.t("tab.records"), "📋"),
         ("permissions", l10n.t("tab.permissions"), "🔐"),
         ("about", l10n.t("tab.about"), "ⓘ")]
    }

    public init() { self.bridge = nil; self.manager = nil; self.records = nil }

    /// Host entry point: pass the SettingsStore-backed bridge + (optionally) the
    /// observable model manager so the Model tab shows live download progress, and
    /// the `records` view rendered in the Records sidebar tab.
    public init(bridge: SettingsBridge,
                manager: (any ModelManagerBridge & ObservableObject)? = nil,
                records: AnyView? = nil) {
        self.bridge = bridge
        self.manager = manager
        self.records = records
    }

    public var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                sidebar
                Rectangle().fill(Vibe.Palette.hairline(scheme)).frame(width: 1)
                content
            }
        }
        .frame(width: 640, height: 560)
        .background(Vibe.Palette.surface(scheme))
        // The NATIVE window title bar shows "偏好设置" + the traffic lights on one row
        // (set up in AppDelegate). No custom title strip here — that produced the
        // "two rows" look. Content sits directly below the native title bar.
        .onAppear {
            if let bridge { s.bind(to: bridge) }
        }
        // Re-sync the controls when the mode changes programmatically (e.g. stopping
        // OnCall reverts the dictation mode) so the picker isn't left stale.
        .onReceive(NotificationCenter.default.publisher(for: Notification.Name("vibeSettingsExternallyChanged"))) { _ in
            if let bridge { s.bind(to: bridge) }
        }
    }

    /// The right-hand pane. Records hosts a self-contained HistoryView (its own
    /// scrolling + frame), so it is rendered WITHOUT the outer padded ScrollView the
    /// other tabs use.
    @ViewBuilder
    private var content: some View {
        if tab == "records" {
            RecordsTab(l10n: l10n, records: records)
                .background(Vibe.Palette.surface(scheme))
        } else {
            ScrollView {
                Group {
                    switch tab {
                    case "general":     GeneralTab(s: s, l10n: l10n)
                    case "dictation":   DictationTab(s: s, l10n: l10n)
                    case "model":       ModelTab(s: s, l10n: l10n,
                                                 relay: ModelManagerRelay(manager))
                    case "permissions": PermissionsTab(s: s, l10n: l10n)
                    default:            AboutTab(l10n: l10n, bridge: bridge)
                    }
                }
                .padding(.vertical, 22).padding(.horizontal, 24)
            }
            .background(Vibe.Palette.surface(scheme))
        }
    }

    private var titlebar: some View {
        // (issue #14) The title sits on the SAME row as the native traffic lights,
        // immediately to their right. No separate coloured band / divider — the row
        // is transparent so the title reads as part of the native title bar (28px =
        // the standard title-bar height; ~78px leading clears the three lights).
        HStack(spacing: 0) {
            Text(l10n.t("settings.title"))
                .font(Vibe.Fonts.ui(13, weight: .semibold))
                .foregroundStyle(Vibe.Palette.textMuted(scheme))
                .padding(.leading, 78)
            Spacer(minLength: 0)
        }
        .frame(height: 28)
        .frame(maxWidth: .infinity)
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack(spacing: 9) {
                RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(Vibe.accentGradient)
                    .frame(width: 22, height: 22)
                    .overlay(LogoBars(heights: [5, 11, 7], barW: 2, gap: 2))
                    .shadow(color: Vibe.Palette.accentA.opacity(0.4), radius: 4, y: 2)
                Text(l10n.t("app.name"))
                    .font(Vibe.Fonts.ui(14, weight: .semibold))
                    .foregroundStyle(Vibe.Palette.text(scheme))
            }
            .padding(.leading, 10).padding(.top, 6).padding(.bottom, 14)

            ForEach(tabs, id: \.id) { t in
                let on = tab == t.id
                Button { tab = t.id } label: {
                    HStack(spacing: 10) {
                        Text(t.icon).font(.system(size: 14)).frame(width: 18)
                        Text(t.label)
                            .font(Vibe.Fonts.ui(13.5, weight: on ? .medium : .regular))
                        Spacer()
                    }
                    .foregroundStyle(Vibe.Palette.text(scheme))
                    .padding(.vertical, 8).padding(.horizontal, 10)
                    // BUGFIX (1a): the WHOLE row is the hit target, not just the
                    // label text. The HStack already fills the width (Spacer),
                    // and contentShape(Rectangle()) makes the transparent gaps
                    // tappable so a click anywhere on the row switches tabs.
                    .contentShape(Rectangle())
                    .background(
                        RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                            .fill(on ? Vibe.Palette.accentSoft(scheme) : .clear)
                    )
                }
                .buttonStyle(.plain)
            }
            Spacer()
        }
        .frame(width: 176)
        .padding(.vertical, 14).padding(.horizontal, 10)
        .background(Vibe.Palette.surface(scheme).opacity(0.7))
    }
}

/// macOS traffic-light dots (decorative).
struct TrafficLights: View {
    var body: some View {
        HStack(spacing: 8) {
            Circle().fill(Color(hex: "#FF5F57")).frame(width: 12, height: 12)
            Circle().fill(Color(hex: "#FEBC2E")).frame(width: 12, height: 12)
            Circle().fill(Color(hex: "#28C840")).frame(width: 12, height: 12)
        }
    }
}

// MARK: - Preview (dark)

#Preview("Settings (dark)") {
    SettingsView()
        .padding(40)
        .background(Color(hex: "#0E0E12"))
        .preferredColorScheme(.dark)
}
