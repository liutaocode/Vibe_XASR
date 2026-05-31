import Foundation
import VibeUI

/// Downloads a streaming latency tier (chunk-<tier>ms) on demand, with live
/// progress, into Application Support/VibeXASR/models/chunk-<tier>ms/.
///
/// Files (per tier, ~615 MB total): encoder-<tier>ms.onnx, decoder-<tier>ms.onnx,
/// joiner-<tier>ms.onnx, tokens.txt — at repo path
/// deployment/models/chunk-<tier>ms-model/<file>.
///
/// Download SOURCE is HuggingFace only (the single verified working mirror):
///   * https://huggingface.co/<repo>/resolve/main/<path>
///
/// (Issue #4) The ModelScope line was removed: `GilgameshWind/X-ASR-zh-en` is not
/// published on ModelScope (verified 2026-05-31 via curl — every owner casing 404s
/// and the model-list search returns 0 hits), so the chooser only ever produced a
/// 404→fallback round-trip. We now build the HuggingFace URL directly with no
/// source picker, no probe, and no fallback branch.
///
/// Each file is downloaded to a temp location, then moved into the tier dir; the
/// whole tier dir is only considered "ready" once all four files are present, so
/// a partial download never satisfies `ModelPaths.tierAvailable`.
@MainActor
final class ModelDownloader: NSObject, ObservableObject, ModelManagerBridge, ModelDownloadSourcing {

    static let shared = ModelDownloader()

    /// Owner/name of the X-ASR streaming model on HuggingFace.
    private let repo = "GilgameshWind/X-ASR-zh-en"            // HuggingFace repo id
    private let hfHost = "https://huggingface.co"

    /// Per-tier download state surfaced to the UI.
    @Published private(set) var progress: [Int: Double] = [:]   // tier → 0...1
    @Published private(set) var active: Set<Int> = []           // tiers downloading
    @Published private(set) var failed: Set<Int> = []           // tiers that errored

    /// Download line is HuggingFace only (ModelScope removed, issue #4). Kept to
    /// satisfy `ModelDownloadSourcing`; the setter is inert (no UI chooser exists).
    @Published var source: ModelDownloadSource = .huggingFace

    /// Bumped whenever an install completes/changes so SwiftUI re-queries
    /// `ModelPaths.tierAvailable` (which reads the filesystem).
    @Published private(set) var installsVersion = 0

    private var tasks: [Int: URLSessionDownloadTask] = [:]
    /// Maps a task → (tier, fileName, remaining files to fetch) for sequencing.
    private final class Job {
        let tier: LatencyTier
        var remaining: [String]
        let destDir: URL
        init(tier: LatencyTier, files: [String], destDir: URL) {
            self.tier = tier; self.remaining = files; self.destDir = destDir
        }
    }
    private var jobs: [Int: Job] = [:]

    override init() {
        super.init()
    }

    private lazy var session: URLSession = {
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForResource = 3600   // big files
        return URLSession(configuration: cfg, delegate: self, delegateQueue: nil)
    }()

    private func files(for tier: LatencyTier) -> [String] {
        ["encoder-\(tier.token)ms.onnx",
         "decoder-\(tier.token)ms.onnx",
         "joiner-\(tier.token)ms.onnx",
         "tokens.txt"]
    }

    /// Repo-relative path of a tier file, shared by both hosts.
    private func relPath(tier: LatencyTier, file: String) -> String {
        "deployment/models/chunk-\(tier.token)ms-model/\(file)"
    }

    /// HuggingFace resolve URL — the single verified source (issue #4).
    private func hfURL(tier: LatencyTier, file: String) -> URL {
        URL(string: "\(hfHost)/\(repo)/resolve/main/\(relPath(tier: tier, file: file))")!
    }

    // MARK: ModelManagerBridge

    func isTierDownloaded(_ tier: LatencyTier) -> Bool {
        _ = installsVersion           // re-read after each install
        return ModelPaths.tierAvailable(tier)
    }
    func isTierBundled(_ tier: LatencyTier) -> Bool { tier.isBundled }
    func downloadProgress(_ tier: LatencyTier) -> Double? {
        active.contains(tier.rawValue) ? (progress[tier.rawValue] ?? 0) : nil
    }
    func didTierFail(_ tier: LatencyTier) -> Bool { failed.contains(tier.rawValue) }

    /// Begin (or resume) downloading a tier. No-op if bundled / already present.
    func startDownload(_ tier: LatencyTier) {
        guard !tier.isBundled, !ModelPaths.tierAvailable(tier),
              !active.contains(tier.rawValue) else { return }
        failed.remove(tier.rawValue)
        let dir = ModelPaths.downloadedTierDir(tier.token)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        // Only fetch files that are not already present (resume across launches).
        let needed = files(for: tier).filter {
            !FileManager.default.fileExists(atPath: dir.appendingPathComponent($0).path)
        }
        guard !needed.isEmpty else { installsVersion += 1; return }

        active.insert(tier.rawValue)
        progress[tier.rawValue] = 0
        let job = Job(tier: tier, files: needed, destDir: dir)
        jobs[tier.rawValue] = job
        log("start tier=\(tier.token)ms via huggingFace — \(needed.count) file(s)")
        fetchNext(job)
    }

    /// Cancel an in-flight tier download.
    func cancelDownload(_ tier: LatencyTier) {
        tasks[tier.rawValue]?.cancel()
        tasks[tier.rawValue] = nil
        jobs[tier.rawValue] = nil
        active.remove(tier.rawValue)
        progress[tier.rawValue] = nil
    }

    /// Delete a downloaded tier's files (frees ~615 MB). No-op for the bundled
    /// tier (it lives in the read-only bundle).
    @discardableResult
    func deleteTier(_ tier: LatencyTier) -> Bool {
        guard !tier.isBundled else { return false }
        cancelDownload(tier)
        let dir = ModelPaths.downloadedTierDir(tier.token)
        // Remove the whole tier dir; if that fails (e.g. partial perms), fall back
        // to deleting each known file so state still flips to "not downloaded".
        do {
            try FileManager.default.removeItem(at: dir)
        } catch {
            for f in files(for: tier) {
                try? FileManager.default.removeItem(at: dir.appendingPathComponent(f))
            }
        }
        failed.remove(tier.rawValue)
        progress[tier.rawValue] = nil
        installsVersion += 1
        log("deleted tier=\(tier.token)ms (downloaded=\(ModelPaths.tierAvailable(tier)))")
        return !ModelPaths.tierAvailable(tier)
    }

    // MARK: sequencing

    private func fetchNext(_ job: Job) {
        guard let file = job.remaining.first else {
            // All files done.
            active.remove(job.tier.rawValue)
            progress[job.tier.rawValue] = 1
            jobs[job.tier.rawValue] = nil
            installsVersion += 1
            log("tier=\(job.tier.token)ms complete")
            return
        }
        start(file: file, for: job, url: hfURL(tier: job.tier, file: file))
    }

    /// Kick off the current file's download task.
    private func start(file: String, for job: Job, url: URL) {
        let task = session.downloadTask(with: url)
        task.taskDescription = "\(job.tier.rawValue)|\(file)"
        tasks[job.tier.rawValue] = task
        task.resume()
    }

    private func handleFinished(tier: Int, file: String, tempURL: URL) {
        guard let job = jobs[tier] else { return }
        let dest = job.destDir.appendingPathComponent(file)
        do {
            try? FileManager.default.removeItem(at: dest)
            try FileManager.default.moveItem(at: tempURL, to: dest)
        } catch {
            fail(tier: tier)
            return
        }
        job.remaining.removeAll { $0 == file }
        fetchNext(job)
    }

    private func fail(tier: Int) {
        active.remove(tier)
        progress[tier] = nil
        jobs[tier] = nil
        tasks[tier] = nil
        failed.insert(tier)
        log("tier=\(tier) FAILED")
    }

    private func log(_ msg: String) {
        FileHandle.standardError.write("[ModelDownloader] \(msg)\n".data(using: .utf8)!)
    }
}

// MARK: - URLSessionDownloadDelegate (nonisolated; hops to main)

extension ModelDownloader: URLSessionDownloadDelegate {

    nonisolated func urlSession(_ session: URLSession,
                                downloadTask: URLSessionDownloadTask,
                                didWriteData bytesWritten: Int64,
                                totalBytesWritten: Int64,
                                totalBytesExpectedToWrite: Int64) {
        guard let desc = downloadTask.taskDescription,
              let tierStr = desc.split(separator: "|").first,
              let tier = Int(tierStr),
              let t = LatencyTier(rawValue: tier),
              totalBytesExpectedToWrite > 0 else { return }
        // Approximate whole-tier progress: completed files + the current one's
        // fraction, averaged over the 4 files of a tier.
        let perFile = Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)
        Task { @MainActor [weak self] in
            guard let self, let job = self.jobs[tier] else { return }
            let all = self.files(for: t).count
            let doneFiles = all - job.remaining.count
            self.progress[tier] = (Double(doneFiles) + perFile) / Double(all)
        }
    }

    nonisolated func urlSession(_ session: URLSession,
                                downloadTask: URLSessionDownloadTask,
                                didFinishDownloadingTo location: URL) {
        guard let desc = downloadTask.taskDescription else { return }
        let parts = desc.split(separator: "|", maxSplits: 1).map(String.init)
        guard parts.count == 2, let tier = Int(parts[0]) else { return }
        let file = parts[1]
        let http = downloadTask.response as? HTTPURLResponse
        let ok = (http?.statusCode ?? 200) < 400
        // Move the temp file synchronously here (it's deleted when this returns).
        // Copy to a stable temp path, then hand off to the main actor.
        let staged = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
        if ok { try? FileManager.default.moveItem(at: location, to: staged) }
        Task { @MainActor [weak self] in
            guard let self else { return }
            if ok {
                self.handleFinished(tier: tier, file: file, tempURL: staged)
            } else {
                try? FileManager.default.removeItem(at: staged)
                self.fail(tier: tier)
            }
        }
    }

    nonisolated func urlSession(_ session: URLSession, task: URLSessionTask,
                                didCompleteWithError error: Error?) {
        guard let error, let desc = task.taskDescription,
              let tierStr = desc.split(separator: "|").first,
              let tier = Int(tierStr) else { return }
        // Ignore explicit cancels (we already cleared state).
        if (error as NSError).code == NSURLErrorCancelled { return }
        Task { @MainActor [weak self] in
            self?.fail(tier: tier)
        }
    }
}
