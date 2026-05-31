import Foundation
import VibeUI

/// Local, on-device dictation history.
///
/// Every dictation final is appended to
/// `~/Library/Application Support/VibeXASR/history.json` — a JSON array of
/// `{text, date}` (date = ISO-8601). File writes go through a private serial
/// `DispatchQueue` so concurrent appends can't corrupt the file; reads tolerate
/// a missing or corrupt file (returning an empty list). Nothing here ever
/// touches the network — the data stays on this device.
///
/// The in-memory `entries` (newest-first) is `@Published` so the History window
/// updates live. `HistoryItem` is the VibeUI-facing row type so the window can
/// render without importing this target.
@MainActor
final class HistoryStore: ObservableObject, HistoryBridge {

    static let shared = HistoryStore()

    /// Newest-first, for display.
    @Published private(set) var entries: [HistoryItem] = []

    /// Lifetime cumulative character count dictated. Persisted separately (in
    /// UserDefaults) so it SURVIVES clearing the history list — it only ever grows.
    @Published private(set) var lifetimeChars: Int = UserDefaults.standard.integer(forKey: lifetimeCharsKey) {
        didSet { UserDefaults.standard.set(lifetimeChars, forKey: Self.lifetimeCharsKey) }
    }
    private static let lifetimeCharsKey = "historyLifetimeChars"

    private let queue = DispatchQueue(label: "com.xasr.vibexasr.history")
    private let fileURL: URL

    private struct Record: Codable {
        var text: String
        var date: Date
        var mode: String?   // "manual" | "oncall"; optional for old files
    }

    init() {
        self.fileURL = ModelPaths.appSupportDir().appendingPathComponent("history.json")
        load()
    }

    // MARK: HistoryBridge (read API for the window)

    var historyItems: [HistoryItem] { entries }

    /// Append a final (called on each engine.onFinal). Ignores blank text.
    /// Seconds an ephemeral (history-disabled) record survives before self-destruct.
    static let ephemeralTTL: TimeInterval = 60

    /// Append a final. When `ephemeral` (history saving is OFF) the entry is kept in
    /// memory only for `ephemeralTTL` seconds (shown with a countdown) then removed,
    /// and never written to disk — a grace buffer so a long unsaved dictation isn't
    /// lost the instant it ends.
    func append(_ text: String, mode: String = "manual", ephemeral: Bool = false) {
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return }
        let expires = ephemeral ? Date().addingTimeInterval(Self.ephemeralTTL) : nil
        // Store the trimmed text — stray leading whitespace (common in ASR output)
        // would indent the row vs its timestamp.
        let item = HistoryItem(id: UUID(), text: trimmed, date: Date(), mode: mode, expiresAt: expires)
        entries.insert(item, at: 0)               // newest first in memory
        lifetimeChars += trimmed.count            // cumulative; survives clearing
        if let expires {
            let id = item.id
            let delay = expires.timeIntervalSinceNow
            DispatchQueue.main.asyncAfter(deadline: .now() + max(0, delay)) { [weak self] in
                self?.entries.removeAll { $0.id == id }   // self-destruct (never persisted)
            }
        } else {
            persistAsync()
        }
    }

    /// Delete a single entry by id.
    func delete(id: UUID) {
        entries.removeAll { $0.id == id }
        persistAsync()
    }

    /// Edit an entry's text in place (issue #6). Keeps id + date; does NOT change
    /// lifetimeChars (which counts what was dictated, not the current edited text).
    func update(id: UUID, text: String) {
        guard let idx = entries.firstIndex(where: { $0.id == id }) else { return }
        let old = entries[idx]
        entries[idx] = HistoryItem(id: old.id, text: text, date: old.date, mode: old.mode, expiresAt: old.expiresAt)
        persistAsync()
    }

    /// Clear all history.
    func clearAll() {
        entries.removeAll()
        lifetimeChars = 0          // clearing records also resets the cumulative stats
        persistAsync()
    }

    // MARK: export (issue #5)

    /// JSON representation of all history (chronological, oldest-first), ready to
    /// write to a user-chosen file via NSSavePanel. The NSSavePanel itself lives in
    /// the AppKit-capable HistoryView.
    func exportJSONData() -> Data {
        let records = entries.reversed().filter { $0.expiresAt == nil }.map { Record(text: $0.text, date: $0.date, mode: $0.mode) }
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return (try? encoder.encode(records)) ?? Data("[]".utf8)
    }

    /// Plain-text representation: one entry per block, "<ISO date>\t<text>"
    /// chronological (oldest-first).
    func exportPlainText() -> String {
        let iso = ISO8601DateFormatter()
        return entries.reversed()
            .map { "\(iso.string(from: $0.date))\t\($0.text)" }
            .joined(separator: "\n")
    }

    // MARK: persistence

    /// Load synchronously at startup; tolerate missing/corrupt file.
    private func load() {
        guard let data = try? Data(contentsOf: fileURL) else { return }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        guard let records = try? decoder.decode([Record].self, from: data) else {
            // Corrupt file — start fresh (don't crash). The next write overwrites it.
            return
        }
        // Stored oldest-first; present newest-first.
        entries = records.reversed().map {
            HistoryItem(id: UUID(), text: $0.text, date: $0.date, mode: $0.mode ?? "manual")
        }
    }

    /// Snapshot the current entries and write them off the main thread.
    private func persistAsync() {
        // Store oldest-first (chronological) so external readers see natural order.
        let records = entries.reversed().filter { $0.expiresAt == nil }.map { Record(text: $0.text, date: $0.date, mode: $0.mode) }
        let url = fileURL
        queue.async {
            let encoder = JSONEncoder()
            encoder.dateEncodingStrategy = .iso8601
            encoder.outputFormatting = [.prettyPrinted]
            guard let data = try? encoder.encode(records) else { return }
            // Atomic write so a crash mid-write can't truncate the file.
            try? data.write(to: url, options: .atomic)
        }
    }
}
