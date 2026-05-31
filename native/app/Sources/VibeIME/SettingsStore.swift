import Foundation
import VibeUI

/// Single source of truth for user-configurable app settings, backed by
/// `UserDefaults`. All keys live under the app's standard suite
/// (`com.xasr.vibexasr`), so the AppDelegate, the SwiftUI Settings window and
/// the onboarding wizard all observe/mutate the same store.
///
/// Keys:
///   * `showDockIcon`          Bool   (default true)   — Dock icon + Launchpad presence.
///   * `hotkeyKeyCode`         Int    (default 54)     — push-to-talk virtual keycode.
///   * `hotkeyModifierOnly`    Bool   (default true)   — is the hotkey a modifier key?
///   * `didCompleteOnboarding` Bool   (default false)  — first-run wizard finished?
///   * `vadKind`               String (default "fire") — "fire" | "silero".
///   * `latencyTier`           Int    (default 960)    — streaming chunk model ms.
///   * `appLanguage`           String (default "auto") — UI language (auto/zh/en/ja/ko).
///   * `padWriteEnabled`       Bool   (default false)  — append finals into the Pad.
///   * `historyEnabled`        Bool   (default true)   — persist finals to history.json.
///   * `insertMethod`          String (default "paste")— "paste" | "type".
///   * `launchAtLogin`         Bool   (default false)  — register a login item.
///
/// Mutations post `SettingsStore.changed` (and more specific notifications) so
/// observers can react live.
@MainActor
final class SettingsStore: L10nPersistence {

    static let shared = SettingsStore()

    /// Posted on any settings mutation routed through this store.
    static let changed = Notification.Name("VibeXASR.settingsChanged")
    /// Posted specifically when the push-to-talk hotkey changes.
    static let hotkeyChanged = Notification.Name("VibeXASR.hotkeyChanged")
    /// Posted when the Dock-icon preference changes.
    static let dockIconChanged = Notification.Name("VibeXASR.dockIconChanged")
    /// Posted when the engine configuration (VAD kind or latency tier) changes,
    /// so the AppDelegate can rebuild the engine off the audio path.
    static let engineConfigChanged = Notification.Name("VibeXASR.engineConfigChanged")
    /// Posted when the launch-at-login preference changes.
    static let launchAtLoginChanged = Notification.Name("VibeXASR.launchAtLoginChanged")

    private enum Key {
        static let showDockIcon = "showDockIcon"
        static let hotkeyKeyCode = "hotkeyKeyCode"
        static let hotkeyModifierOnly = "hotkeyModifierOnly"
        static let didCompleteOnboarding = "didCompleteOnboarding"
        static let vadKind = "vadKind"
        static let latencyTier = "latencyTier"
        static let appLanguage = "appLanguage"
        static let padWriteEnabled = "padWriteEnabled"
        static let historyEnabled = "historyEnabled"
        static let insertMethod = "insertMethod"
        static let clipboardOverwrite = "clipboardOverwrite"
        static let launchAtLogin = "launchAtLogin"
    }

    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
        defaults.register(defaults: [
            Key.showDockIcon: true,
            Key.hotkeyKeyCode: 54,            // Right ⌘
            Key.hotkeyModifierOnly: true,
            Key.didCompleteOnboarding: false,
            Key.vadKind: "fire",
            Key.latencyTier: 960,
            Key.appLanguage: "auto",
            Key.padWriteEnabled: false,
            Key.historyEnabled: true,
            Key.insertMethod: "type",   // default to streaming keystroke insertion
            Key.clipboardOverwrite: false,
            Key.launchAtLogin: false,
        ])
    }

    // MARK: Dock icon

    var showDockIcon: Bool {
        get { defaults.bool(forKey: Key.showDockIcon) }
        set {
            defaults.set(newValue, forKey: Key.showDockIcon)
            post(SettingsStore.dockIconChanged)
        }
    }

    // MARK: Push-to-talk hotkey

    var hotkeyKeyCode: Int {
        get { defaults.integer(forKey: Key.hotkeyKeyCode) }
        set { defaults.set(newValue, forKey: Key.hotkeyKeyCode) }
    }

    var hotkeyModifierOnly: Bool {
        get { defaults.bool(forKey: Key.hotkeyModifierOnly) }
        set { defaults.set(newValue, forKey: Key.hotkeyModifierOnly) }
    }

    /// Update the hotkey atomically and notify observers.
    func setHotkey(keyCode: Int, modifierOnly: Bool) {
        defaults.set(keyCode, forKey: Key.hotkeyKeyCode)
        defaults.set(modifierOnly, forKey: Key.hotkeyModifierOnly)
        post(SettingsStore.hotkeyChanged)
    }

    // MARK: Onboarding

    var didCompleteOnboarding: Bool {
        get { defaults.bool(forKey: Key.didCompleteOnboarding) }
        set {
            defaults.set(newValue, forKey: Key.didCompleteOnboarding)
            post(SettingsStore.changed)
        }
    }

    // MARK: VAD + latency tier (engine config)

    /// "fire" (FireRedVAD, default) or "silero".
    var vadKind: String {
        get { defaults.string(forKey: Key.vadKind) ?? "fire" }
        set {
            guard newValue != vadKind else { return }
            defaults.set(newValue, forKey: Key.vadKind)
            post(SettingsStore.engineConfigChanged)
        }
    }

    /// Streaming chunk model size in ms (160/480/960/1920). Defaults to 960.
    var latencyTier: Int {
        get {
            let v = defaults.integer(forKey: Key.latencyTier)
            return LatencyTier(rawValue: v) != nil ? v : 960
        }
        set {
            guard newValue != latencyTier, LatencyTier(rawValue: newValue) != nil else { return }
            defaults.set(newValue, forKey: Key.latencyTier)
            post(SettingsStore.engineConfigChanged)
        }
    }

    var latencyTierEnum: LatencyTier { LatencyTier(rawValue: latencyTier) ?? .ms960 }

    // MARK: UI language (L10nPersistence)

    /// Raw language choice (auto/zh/en/ja/ko). Conforms to `L10nPersistence` so
    /// VibeUI's `L10n` can read/write it without importing this type.
    var storedLang: String {
        get { defaults.string(forKey: Key.appLanguage) ?? "auto" }
        set {
            defaults.set(newValue, forKey: Key.appLanguage)
            post(SettingsStore.changed)
        }
    }

    // MARK: Pad / History

    /// When on, every dictation final is appended to the built-in Pad.
    var padWriteEnabled: Bool {
        get { defaults.bool(forKey: Key.padWriteEnabled) }
        set { defaults.set(newValue, forKey: Key.padWriteEnabled); post(SettingsStore.changed) }
    }

    /// When on, every dictation final is persisted to history.json.
    var historyEnabled: Bool {
        get { defaults.bool(forKey: Key.historyEnabled) }
        set { defaults.set(newValue, forKey: Key.historyEnabled); post(SettingsStore.changed) }
    }

    // MARK: Insert method

    /// "paste" (clipboard + ⌘V) or "type" (synthesize keystrokes).
    var insertMethod: String {
        get { defaults.string(forKey: Key.insertMethod) ?? "type" }
        set { defaults.set(newValue, forKey: Key.insertMethod); post(SettingsStore.changed) }
    }

    /// When on, each dictation also overwrites the system clipboard with the final
    /// text (so it can be pasted anywhere). Default off.
    var clipboardOverwrite: Bool {
        get { defaults.bool(forKey: Key.clipboardOverwrite) }
        set { defaults.set(newValue, forKey: Key.clipboardOverwrite); post(SettingsStore.changed) }
    }

    // MARK: Launch at login

    var launchAtLogin: Bool {
        get { defaults.bool(forKey: Key.launchAtLogin) }
        set {
            defaults.set(newValue, forKey: Key.launchAtLogin)
            post(SettingsStore.launchAtLoginChanged)
        }
    }

    // MARK: -

    private func post(_ name: Notification.Name) {
        NotificationCenter.default.post(name: name, object: self)
        NotificationCenter.default.post(name: SettingsStore.changed, object: self)
    }
}
