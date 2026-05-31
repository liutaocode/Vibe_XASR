// ============================================================
//  Vibe XASR — History window
//  Lists every dictation final newest-first with timestamps. Per-row copy +
//  delete, plus "清空/Clear all". A prominent bilingual privacy line sits at the
//  top: "您的数据永远保存在本地,绝不上云 · Your data stays on this device — never uploaded."
//  Localized via L10n (the privacy line is always shown bilingually).
// ============================================================

import SwiftUI
import AppKit
import UniformTypeIdentifiers

/// History window content. Generic over the host's history store (an
/// ObservableObject conforming to HistoryBridge) so the list refreshes live as
/// new finals are appended.
public struct HistoryView<Store: HistoryBridge & ObservableObject>: View {
    @ObservedObject private var store: Store
    @ObservedObject private var l10n: L10n
    @Environment(\.colorScheme) private var scheme

    @State private var toastID: UUID?
    @State private var showAll = false
    @State private var showClearConfirm = false
    /// Id of the row currently being edited inline (issue #6), plus its draft text.
    @State private var editingID: UUID?
    @State private var draft = ""
    /// Id of the row currently hovered — used to reveal the (otherwise hidden)
    /// trailing copy/edit/delete cluster only on hover, so rows are clean at rest.
    @State private var hoverID: UUID?
    /// Show entries recorded in OnCall (always-on) mode. Hidden by default so the
    /// list (and export) only show deliberate push-to-talk dictations unless the
    /// user opts in. Persisted across launches.
    @AppStorage("historyShowOnCall") private var showOnCall = false

    public init(store: Store, l10n: L10n = .shared) {
        self.store = store
        self.l10n = l10n
    }

    /// Entries currently shown, honoring the OnCall filter. Export + list + the
    /// empty state all derive from this so they stay in lock-step.
    private var filteredItems: [HistoryItem] {
        showOnCall ? store.historyItems
                   : store.historyItems.filter { $0.mode != "oncall" }
    }

    /// True when the store has any oncall entries (so the toggle is worth showing).
    private var hasOnCall: Bool {
        store.historyItems.contains { $0.mode == "oncall" }
    }

    public var body: some View {
        VStack(spacing: 0) {
            header
            statsBar
            privacyBanner
            Divider().overlay(Vibe.Palette.hairline(scheme))
            if filteredItems.isEmpty {
                emptyState
            } else {
                list
            }
        }
        .frame(minWidth: 360, minHeight: 320)   // adapt to the Settings pane (no 480 overflow)
        .background(Vibe.Palette.surface(scheme))
        // Clearing is destructive (deletes all history + resets the cumulative stats),
        // so it requires explicit confirmation.
        .confirmationDialog(tt("确定清空全部记录?", "Clear all records?",
                                "すべての記録を消去しますか?", "모든 기록을 지울까요?"),
                            isPresented: $showClearConfirm, titleVisibility: .visible) {
            Button(tt("清空(不可恢复)", "Clear all (can't undo)",
                      "消去(取り消せません)", "지우기(되돌릴 수 없음)"), role: .destructive) {
                store.clearAll()
            }
            Button(tt("取消", "Cancel", "キャンセル", "취소"), role: .cancel) {}
        } message: {
            Text(tt("将永久删除全部历史记录,并把累计字数/节省时间清零,无法恢复。",
                    "Permanently deletes all history and resets the cumulative stats. This can't be undone.",
                    "すべての履歴を完全に削除し、累計の文字数/節約時間をリセットします。取り消せません。",
                    "모든 기록을 영구 삭제하고 누적 글자수/절약 시간을 초기화합니다. 되돌릴 수 없습니다."))
        }
    }

    private var header: some View {
        HStack {
            HStack(spacing: 9) {
                RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(Vibe.accentGradient)
                    .frame(width: 22, height: 22)
                    .overlay(LogoBars(heights: [5, 11, 7], barW: 2, gap: 2))
                Text(l10n.t("history.title"))
                    .font(Vibe.Fonts.ui(14, weight: .semibold))
                    .foregroundStyle(Vibe.Palette.text(scheme))
                Text(String(format: l10n.t("history.count"), store.historyItems.count))
                    .font(Vibe.Fonts.mono(11))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
            }
            Spacer()
            if hasOnCall {
                OnCallToggle(on: $showOnCall, label: l10n.t("history.showOnCall"))
            }
            if !store.historyItems.isEmpty {
                MButton(title: l10n.t("history.export"), kind: .ghost) { exportVisible() }
                MButton(title: l10n.t("clear.all"), kind: .danger) {
                    showClearConfirm = true
                }
            }
        }
        .padding(.horizontal, 16).padding(.vertical, 12)
        .background(Vibe.Palette.surface2(scheme))
    }

    /// (issue #9) Compact stats: cumulative characters dictated + estimated typing
    /// time saved. Assumes a 200 chars/min typing speed for the "time saved" figure.
    @ViewBuilder
    private var statsBar: some View {
        let chars = store.lifetimeChars
        if chars > 0 {
            HStack(spacing: 6) {
                Text("📊")
                Text(statsText(chars: chars))
                    .font(Vibe.Fonts.ui(12, weight: .medium))
                    .foregroundStyle(Vibe.Palette.text(scheme))
                Spacer()
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.horizontal, 16).padding(.vertical, 8)
            .background(Vibe.Palette.accentSoft(scheme))
        }
    }

    /// Build the localized stats line. 200 chars/min ⇒ minutes = chars/200.
    /// Big-milestone short form when chars>10000 AND savedHours>100.
    private func statsText(chars: Int) -> String {
        let savedMinutes = Double(chars) / 200.0
        let savedHours = savedMinutes / 60.0
        if chars > 10_000 && savedHours > 100 {
            return l10n.t("history.stats.big")
        }
        let charsPart = String(format: l10n.t("history.stats.chars"), grouped(chars))
        let timePart: String
        if savedHours >= 1 {
            timePart = String(format: l10n.t("history.stats.hours"),
                              String(format: "%.1f", savedHours))
        } else {
            timePart = String(format: l10n.t("history.stats.minutes"),
                              String(savedMinutes < 1 ? "<1" : "\(Int(savedMinutes))"))
        }
        return charsPart + timePart
    }

    private func grouped(_ n: Int) -> String {
        let f = NumberFormatter()
        f.numberStyle = .decimal
        return f.string(from: NSNumber(value: n)) ?? "\(n)"
    }

    /// (issue #5) Export the CURRENTLY VISIBLE history (i.e. honoring the OnCall
    /// filter — oncall entries are excluded unless the toggle is on) to a
    /// user-chosen file (JSON default, or .txt). The payload is built here from the
    /// filtered items rather than the store's export-all so what you see is what you
    /// get.
    private func exportVisible() {
        let panel = NSSavePanel()
        panel.title = l10n.t("history.export.panel")
        panel.nameFieldStringValue = "vibe-xasr-history.json"
        panel.allowedContentTypes = [.json, .plainText]
        panel.isExtensionHidden = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        let items = filteredItems
        let isText = url.pathExtension.lowercased() == "txt"
        let data = isText ? Data(exportPlainText(items).utf8) : exportJSONData(items)
        try? data.write(to: url, options: .atomic)
    }

    /// JSON array of `{date, text, mode}` for the given (already filtered) items.
    private func exportJSONData(_ items: [HistoryItem]) -> Data {
        let iso = ISO8601DateFormatter()
        let arr: [[String: String]] = items.map {
            ["date": iso.string(from: $0.date), "text": $0.text, "mode": $0.mode]
        }
        let opts: JSONSerialization.WritingOptions = [.prettyPrinted, .sortedKeys]
        return (try? JSONSerialization.data(withJSONObject: arr, options: opts))
            ?? Data("[]".utf8)
    }

    /// Plain-text form of the given (already filtered) items: timestamp + text.
    private func exportPlainText(_ items: [HistoryItem]) -> String {
        items.map { "\(historyDateFormatter.string(from: $0.date))\n\($0.text)" }
            .joined(separator: "\n\n")
    }

    /// Always-bilingual privacy reassurance — the load-bearing trust line.
    private var privacyBanner: some View {
        HStack(spacing: 8) {
            Text("🔒")
            VStack(alignment: .leading, spacing: 1) {
                Text("您的数据永远保存在本地,绝不上云")
                    .font(Vibe.Fonts.ui(12.5, weight: .semibold))
                    .foregroundStyle(Vibe.Palette.success)
                Text("Your data stays on this device — never uploaded.")
                    .font(Vibe.Fonts.ui(11.5))
                    .foregroundStyle(Vibe.Palette.textMuted(scheme))
            }
            Spacer()
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .padding(.horizontal, 14).padding(.vertical, 10)
        .background(Vibe.Palette.success.opacity(0.12))
    }

    private var emptyState: some View {
        VStack(spacing: 8) {
            Spacer()
            Text("🗒")
                .font(.system(size: 40))
                .opacity(0.5)
            Text(l10n.t("history.empty"))
                .font(Vibe.Fonts.ui(13))
                .foregroundStyle(Vibe.Palette.textMuted(scheme))
            Spacer()
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Vibe.Palette.surface(scheme))
    }

    private var list: some View {
        // Newest-first; show the recent 10 by default and paginate the rest.
        // `filteredItems` already drops OnCall entries unless the toggle is on.
        let items = filteredItems
        let visible = showAll ? items : Array(items.prefix(10))
        let hidden = items.count - visible.count
        return ScrollView {
            LazyVStack(spacing: 1) {
                ForEach(visible) { item in
                    row(item)
                }
                if hidden > 0 {
                    moreRow(symbol: "chevron.down",
                            label: tt("显示更早的 \(hidden) 条", "Show \(hidden) earlier",
                                      "さらに \(hidden) 件", "이전 \(hidden)개")) {
                        withAnimation { showAll = true }
                    }
                } else if showAll && items.count > 10 {
                    moreRow(symbol: "chevron.up",
                            label: tt("收起", "Collapse", "折りたたむ", "접기")) {
                        withAnimation { showAll = false }
                    }
                }
            }
            .background(Vibe.Palette.hairline(scheme))
        }
        .background(Vibe.Palette.surface(scheme))
    }

    /// "Show earlier / collapse" pagination affordance (recent-10 default).
    private func moreRow(symbol: String, label: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Image(systemName: symbol).font(.system(size: 11, weight: .semibold))
                Text(label).font(Vibe.Fonts.ui(12, weight: .medium))
            }
            .foregroundStyle(Vibe.Palette.accentB)
            .frame(maxWidth: .infinity)
            .padding(.vertical, 12)
            .background(Vibe.Palette.surface(scheme))
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    /// Local 4-language pick for the two pagination strings, so this view needs no
    /// new keys in the shared L10n table. resolved language → string.
    private func tt(_ zh: String, _ en: String, _ ja: String, _ ko: String) -> String {
        switch l10n.resolved {
        case .zh: return zh
        case .ja: return ja
        case .ko: return ko
        default:  return en
        }
    }

    @ViewBuilder
    private func row(_ item: HistoryItem) -> some View {
        let isEditing = editingID == item.id
        let isHovering = hoverID == item.id
        // The cluster is shown while hovering OR while this row is being edited
        // (so the checkmark/commit affordance stays reachable).
        let showActions = isHovering || isEditing
        HStack(alignment: .top, spacing: 12) {
            // (issue #3) Entry text + timestamp share the SAME leading edge and are
            // tightly stacked top-aligned — no offset between them.
            VStack(alignment: .leading, spacing: 4) {
                if isEditing {
                    // (issue #6) Inline editable field; Enter / Save commits.
                    TextField("", text: $draft, axis: .vertical)
                        .textFieldStyle(.plain)
                        .font(Vibe.Fonts.mono(13))
                        .foregroundStyle(Vibe.Palette.text(scheme))
                        .padding(.vertical, 5).padding(.horizontal, 8)
                        .background(
                            RoundedRectangle(cornerRadius: 6, style: .continuous)
                                .fill(Vibe.Palette.surface2(scheme)))
                        .overlay(
                            RoundedRectangle(cornerRadius: 6, style: .continuous)
                                .strokeBorder(Vibe.Palette.accentA, lineWidth: 1))
                        .onSubmit { commitEdit(item) }
                } else {
                    // Text starts flush-left so it lines up with the meta row below.
                    // Trim stray leading/trailing whitespace that would indent it.
                    Text(item.text.trimmingCharacters(in: .whitespacesAndNewlines))
                        .font(Vibe.Fonts.mono(13))
                        .foregroundStyle(Vibe.Palette.text(scheme))
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .textSelection(.enabled)
                }
                // Meta row (same leading edge as the text): timestamp · countdown · badge.
                HStack(spacing: 6) {
                    Text(historyDateFormatter.string(from: item.date))
                        .font(Vibe.Fonts.mono(10.5))
                        .foregroundStyle(Vibe.Palette.textMuted(scheme))
                    if let exp = item.expiresAt {
                        ExpiryCountdown(expiresAt: exp)   // ephemeral: self-destructs at countdown end
                    }
                    if item.mode == "oncall" { OnCallBadge() }
                }
            }
            // (issue #2) Fixed-width trailing zone. Reserving the space here means
            // revealing/hiding the cluster on hover never changes row width or
            // nudges the list. The cluster is an overlay so it can't affect layout.
            Color.clear
                .frame(width: historyActionZoneWidth)
                .overlay(alignment: .top) {
                    actionCluster(item, isEditing: isEditing)
                        .opacity(showActions ? 1 : 0)
                        .allowsHitTesting(showActions)
                }
        }
        .padding(.vertical, 11).padding(.horizontal, 16)
        .background(Vibe.Palette.surface(scheme))
        .contentShape(Rectangle())
        .onHover { inside in
            hoverID = inside ? item.id : (hoverID == item.id ? nil : hoverID)
        }
        // (issue #2) The "copied" toast is an OVERLAY pinned to the trailing edge,
        // so it never inserts into the text stack and can't change row height.
        .overlay(alignment: .topTrailing) {
            if toastID == item.id {
                Text(l10n.t("history.copied"))
                    .font(Vibe.Fonts.ui(10.5, weight: .semibold))
                    .foregroundStyle(Vibe.Palette.success)
                    .padding(.vertical, 3).padding(.horizontal, 8)
                    .background(Capsule().fill(Vibe.Palette.success.opacity(0.15)))
                    .padding(.top, 9).padding(.trailing, 16)
                    .transition(.opacity)
                    .allowsHitTesting(false)
            }
        }
    }

    /// The compact trailing action cluster, revealed only on hover (or while
    /// editing). Tidy, fixed-metric — fits inside `actionZoneWidth`.
    @ViewBuilder
    private func actionCluster(_ item: HistoryItem, isEditing: Bool) -> some View {
        HStack(spacing: 4) {
            if isEditing {
                IconButton(symbol: "checkmark") { commitEdit(item) }
            } else {
                IconButton(symbol: "doc.on.doc") { copy(item) }
                IconButton(symbol: "pencil") { beginEdit(item) }
                IconButton(symbol: "trash", danger: true) { store.delete(id: item.id) }
            }
        }
        .frame(maxWidth: historyActionZoneWidth, alignment: .trailing)
    }

    private func beginEdit(_ item: HistoryItem) {
        draft = item.text
        editingID = item.id
    }

    private func commitEdit(_ item: HistoryItem) {
        let trimmed = draft.trimmingCharacters(in: .whitespacesAndNewlines)
        if !trimmed.isEmpty { store.update(id: item.id, text: draft) }
        editingID = nil
    }

    private func copy(_ item: HistoryItem) {
        let pb = NSPasteboard.general
        pb.clearContents()
        pb.setString(item.text, forType: .string)
        withAnimation { toastID = item.id }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
            withAnimation { if toastID == item.id { toastID = nil } }
        }
    }

}

/// Fixed width reserved for the trailing action cluster so rows NEVER reflow
/// whether the cluster is hidden (at rest) or revealed (on hover / while editing).
/// 3 compact 26pt buttons + gaps ≈ 90pt. Free-standing because static stored
/// props aren't allowed in the generic `HistoryView<Store>`.
private let historyActionZoneWidth: CGFloat = 92

/// Shared formatter (free-standing — static stored props aren't allowed in a
/// generic type like `HistoryView<Store>`).
private let historyDateFormatter: DateFormatter = {
    let f = DateFormatter()
    f.dateStyle = .medium
    f.timeStyle = .short
    return f
}()

/// Small SF-symbol icon button used by the history rows.
private struct IconButton: View {
    @Environment(\.colorScheme) private var scheme
    var symbol: String
    var danger: Bool = false
    var action: () -> Void
    @State private var hovering = false
    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 11.5))
                .foregroundStyle(hovering
                                 ? (danger ? Vibe.Palette.error : Vibe.Palette.accentB)
                                 : Vibe.Palette.textMuted(scheme))
                .frame(width: 26, height: 24)
                .background(
                    RoundedRectangle(cornerRadius: 6, style: .continuous)
                        .fill(hovering ? Vibe.Palette.surface2(scheme) : .clear)
                )
        }
        .buttonStyle(.plain)
        .onHover { hovering = $0 }
    }
}

/// Small "OnCall" pill shown on history rows recorded in always-on standby mode.
private struct OnCallBadge: View {
    @Environment(\.colorScheme) private var scheme
    @ObservedObject private var l10n = L10n.shared
    var body: some View {
        Text(l10n.t("history.oncall.badge"))
            .font(Vibe.Fonts.ui(9.5, weight: .semibold))
            .foregroundStyle(Vibe.Palette.accentB)
            .padding(.vertical, 1.5).padding(.horizontal, 6)
            .background(Capsule().fill(Vibe.Palette.accentB.opacity(0.16)))
            .overlay(Capsule().strokeBorder(Vibe.Palette.accentB.opacity(0.35), lineWidth: 1))
            .fixedSize()
    }
}

/// Live countdown for an ephemeral (history-off) record — ticks down to 0, at which
/// point the store removes the row. Tells the user the record is about to vanish.
private struct ExpiryCountdown: View {
    let expiresAt: Date
    var body: some View {
        TimelineView(.periodic(from: .now, by: 1)) { ctx in
            let remain = max(0, Int(expiresAt.timeIntervalSince(ctx.date).rounded(.up)))
            Text("⏳ \(remain)s")
                .font(Vibe.Fonts.mono(10.5))
                .foregroundStyle(Vibe.Palette.error)
                .fixedSize()
        }
    }
}

/// Compact header toggle for "Show OnCall content". A tidy checkbox-style pill so
/// it sits comfortably beside the export / clear buttons.
private struct OnCallToggle: View {
    @Environment(\.colorScheme) private var scheme
    @Binding var on: Bool
    var label: String
    var body: some View {
        Button {
            on.toggle()
        } label: {
            HStack(spacing: 6) {
                Image(systemName: on ? "checkmark.square.fill" : "square")
                    .font(.system(size: 12))
                    .foregroundStyle(on ? Vibe.Palette.accentB : Vibe.Palette.textMuted(scheme))
                Text(label)
                    .font(Vibe.Fonts.ui(12))
                    .foregroundStyle(on ? Vibe.Palette.text(scheme) : Vibe.Palette.textMuted(scheme))
            }
            .padding(.vertical, 5).padding(.horizontal, 10)
            .background(
                RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                    .fill(on ? Vibe.Palette.accentSoft(scheme) : .clear)
            )
            .overlay(
                RoundedRectangle(cornerRadius: Vibe.Radius.control, style: .continuous)
                    .strokeBorder(Vibe.Palette.hairline(scheme), lineWidth: 1)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }
}
