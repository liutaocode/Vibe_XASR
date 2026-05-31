// ============================================================
//  Vibe XASR — Host bridges for the VibeUI windows
//
//  The VibeUI library stays framework-light (SwiftUI only). The host app
//  (VibeIME) implements these protocols to provide real model-management,
//  history and pad behaviour to the Settings / History / Pad surfaces. Concrete
//  stores live in the app target; these are the read/write seams.
// ============================================================

import Foundation

// MARK: - Model management (Model tab)

/// Live model-management state for the latency-tier rows. The host's
/// ModelDownloader conforms to this; the Model tab observes it (the host passes
/// an ObservableObject so SwiftUI refreshes on download progress).
@MainActor
public protocol ModelManagerBridge: AnyObject {
    func isTierDownloaded(_ tier: LatencyTier) -> Bool
    func isTierBundled(_ tier: LatencyTier) -> Bool
    /// 0...1 while downloading, nil otherwise.
    func downloadProgress(_ tier: LatencyTier) -> Double?
    func didTierFail(_ tier: LatencyTier) -> Bool

    func startDownload(_ tier: LatencyTier)
    func cancelDownload(_ tier: LatencyTier)
    @discardableResult func deleteTier(_ tier: LatencyTier) -> Bool
}

// MARK: - History window

/// One persisted dictation final.
public struct HistoryItem: Identifiable, Sendable, Equatable {
    public let id: UUID
    public let text: String
    public let date: Date
    /// "manual" (push-to-talk) or "oncall" (always-on standby). Used to badge and
    /// optionally hide OnCall entries in the list + export.
    public let mode: String
    /// When set, this is an EPHEMERAL record (history saving was off) that self-
    /// destructs at this instant; the row shows a live countdown. nil = permanent.
    public let expiresAt: Date?
    public init(id: UUID, text: String, date: Date, mode: String = "manual", expiresAt: Date? = nil) {
        self.id = id; self.text = text; self.date = date; self.mode = mode; self.expiresAt = expiresAt
    }
}

/// Read/mutate the local dictation history. The host's HistoryStore conforms.
@MainActor
public protocol HistoryBridge: AnyObject {
    var historyItems: [HistoryItem] { get }
    func delete(id: UUID)
    func clearAll()

    /// Cumulative characters ever dictated. Persists across clearing the list
    /// (issue #9). Default 0 so previews / older hosts compile.
    var lifetimeChars: Int { get }

    /// Edit an entry in place (issue #6). Default no-op for previews.
    func update(id: UUID, text: String)

    /// Serialize ALL history for "export all" (issue #5). The view writes the
    /// returned data via NSSavePanel. Defaults produce empty payloads in previews.
    func exportJSONData() -> Data
    func exportPlainText() -> String
}

public extension HistoryBridge {
    var lifetimeChars: Int { 0 }
    func update(id: UUID, text: String) {}
    func exportJSONData() -> Data { Data("[]".utf8) }
    func exportPlainText() -> String { "" }
}

// MARK: - Pad window

/// Two-way access to the built-in Pad text + helpers. The host's PadStore
/// conforms (an ObservableObject so the editor stays in sync with appends).
@MainActor
public protocol PadBridge: AnyObject {
    var padText: String { get set }
    func clear()
}
