/* ============================================================
   Vibe IME — Preferences window
   General / Dictation / Model / Permissions / About
   全控件态:toggle / 下拉 / 按键录制 / 进度条 / 状态 pill / 词典
   ============================================================ */
const { useState, useRef, useEffect } = React;

/* ---------- 原子控件 ---------- */
function Toggle({ on, onChange }) {
  return (
    <button className={"sw" + (on ? " on" : "")} role="switch" aria-checked={on}
      onClick={() => onChange(!on)}><span className="knob" /></button>
  );
}

function Seg({ value, options, onChange }) {
  return (
    <div className="seg">
      {options.map(([v, l]) => (
        <button key={v} className={value === v ? "on" : ""} onClick={() => onChange(v)}>{l}</button>
      ))}
    </div>
  );
}

function Select({ value, options, onChange }) {
  return (
    <div className="sel-wrap">
      <select className="sel" value={value} onChange={(e) => onChange(e.target.value)}>
        {options.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
      </select>
      <span className="sel-chev">⌄</span>
    </div>
  );
}

function KeyRecorder({ combo, onChange }) {
  const [rec, setRec] = useState(false);
  useEffect(() => {
    if (!rec) return;
    const onKey = (e) => {
      e.preventDefault();
      const mods = [];
      if (e.metaKey) mods.push("⌘");
      if (e.ctrlKey) mods.push("⌃");
      if (e.altKey) mods.push("⌥");
      if (e.shiftKey) mods.push("⇧");
      let key = e.key;
      if (key === " ") key = "Space";
      if (!["Meta", "Control", "Alt", "Shift"].includes(e.key)) {
        onChange([...mods, key.length === 1 ? key.toUpperCase() : key].join(" "));
        setRec(false);
      } else if (mods.length) {
        onChange(mods.join(" "));
      }
    };
    const onUp = () => setRec(false);
    window.addEventListener("keydown", onKey);
    window.addEventListener("keyup", onUp);
    return () => { window.removeEventListener("keydown", onKey); window.removeEventListener("keyup", onUp); };
  }, [rec]);
  return (
    <button className={"keyrec" + (rec ? " rec" : "")} onClick={() => setRec(true)}>
      {rec ? "按下按键…" : <span className="kbd">{combo}</span>}
    </button>
  );
}

function StatusPill({ ok }) {
  return ok
    ? <span className="pill ok">✓ 已授权</span>
    : <span className="pill bad">✕ 未授权</span>;
}

/* ---------- row 容器 ---------- */
function Row({ title, help, children, control }) {
  return (
    <div className="row">
      <div className="row-main">
        <div className="row-text">
          <div className="row-title">{title}</div>
          {help && <div className="row-help">{help}</div>}
        </div>
        <div className="row-ctl">{control}</div>
      </div>
      {children}
    </div>
  );
}

function Group({ label, children }) {
  return (
    <div className="grp">
      {label && <div className="grp-label mono">{label}</div>}
      <div className="grp-body">{children}</div>
    </div>
  );
}

/* ---------- 各 Tab ---------- */
function General({ s, set }) {
  return (
    <Group label="GENERAL">
      <Row title="开机自启动" help="登录时在后台静默启动 Vibe IME"
        control={<Toggle on={s.autostart} onChange={(v) => set("autostart", v)} />} />
      <Row title="菜单栏图标样式" help="单色字形更贴合系统外观;Emoji 更直观"
        control={<Seg value={s.glyph} options={[["emoji", "Emoji"], ["mono", "单色字形"]]} onChange={(v) => set("glyph", v)} />} />
      <Row title="主题" help="跟随系统会随明暗自动切换"
        control={<Seg value={s.theme} options={[["auto", "跟随系统"], ["dark", "深"], ["light", "浅"]]} onChange={(v) => set("theme", v)} />} />
      <Row title="界面语言"
        control={<Select value={s.lang} options={[["zh", "中文"], ["en", "English"]]} onChange={(v) => set("lang", v)} />} />
    </Group>
  );
}

function Dictation({ s, set }) {
  const [dict, setDict] = useState([
    { from: "克劳德", to: "Claude" },
    { from: "逗号", to: "," },
    { from: "井号", to: "#" },
  ]);
  const upd = (i, k, v) => setDict(d => d.map((x, j) => j === i ? { ...x, [k]: v } : x));
  return (
    <React.Fragment>
      <Group label="DICTATION">
        <Row title="触发键(push-to-talk)" help="点一下开始录制,然后按下你想用的键"
          control={<KeyRecorder combo={s.combo} onChange={(v) => set("combo", v)} />} />
        <Row title="触发模式" help="按住说话最直觉;免提用 VAD 自动断句"
          control={<Seg value={s.mode} options={[["hold", "按住说话"], ["toggle", "开关"], ["hands", "免提"]]} onChange={(v) => set("mode", v)} />} />
        <Row title="插入方式" help="粘贴更快;逐字更兼容老应用"
          control={<Seg value={s.insert} options={[["paste", "粘贴 ⌘V"], ["type", "逐字"]]} onChange={(v) => set("insert", v)} />} />
        <Row title="自动标点 & 大小写" help="根据语气自动补标点、修正英文大小写"
          control={<Toggle on={s.autopunct} onChange={(v) => set("autopunct", v)} />} />
        <Row title="句末行为" help="整句插入更稳;实时插入更跟手"
          control={<Seg value={s.eos} options={[["whole", "整句插入"], ["live", "实时插入"]]} onChange={(v) => set("eos", v)} />} />
      </Group>

      <Group label="自定义替换词典 · vibecoding 术语">
        <div className="dict">
          <div className="dict-head">
            <span>听到</span><span></span><span>替换为</span><span></span>
          </div>
          {dict.map((d, i) => (
            <div className="dict-row" key={i}>
              <input className="inp" value={d.from} onChange={(e) => upd(i, "from", e.target.value)} />
              <span className="arr">→</span>
              <input className="inp" value={d.to} onChange={(e) => upd(i, "to", e.target.value)} />
              <button className="del" onClick={() => setDict(dict.filter((_, j) => j !== i))}>✕</button>
            </div>
          ))}
          <button className="add" onClick={() => setDict([...dict, { from: "", to: "" }])}>+ 添加替换</button>
        </div>
      </Group>
    </React.Fragment>
  );
}

const MODELS = [
  { id: "fr-vad", name: "FireRedVAD", tag: "VAD · 默认", size: "12 MB", state: "done" },
  { id: "silero", name: "silero-vad", tag: "VAD", size: "8 MB", state: "none" },
  { id: "xasr-zh", name: "X-ASR zh-en", tag: "ASR · 中英", size: "1.1 GB", state: "done" },
  { id: "xasr-lg", name: "X-ASR zh-en large", tag: "ASR · 更准", size: "2.3 GB", state: "downloading", pct: 64 },
];

function Model({ s, set }) {
  const [models, setModels] = useState(MODELS);
  const act = (id) => setModels(m => m.map(x => x.id === id
    ? { ...x, state: x.state === "done" ? "none" : "done" } : x));
  return (
    <React.Fragment>
      <Group label="VAD / ASR">
        <Row title="语音活动检测 (VAD)" help="检测「在说话 / 静音」,决定何时断句"
          control={<Select value={s.vad} options={[["fire", "FireRedVAD(默认)"], ["silero", "silero"]]} onChange={(v) => set("vad", v)} />} />
        <Row title="延迟档" help="越小越快、越大越准。当前每段约 480ms"
          control={<Seg value={s.latency} options={[["160", "160"], ["480", "480"], ["960", "960"], ["1920", "1920"]]} onChange={(v) => set("latency", v)} />} />
        <Row title="推理 Provider" help="CoreML 走神经引擎更省电"
          control={<Seg value={s.provider} options={[["cpu", "CPU"], ["coreml", "CoreML"]]} onChange={(v) => set("provider", v)} />} />
        <Row title="识别语言"
          control={<Select value={s.aslang} options={[["zh-en", "中英 (zh-en)"]]} onChange={(v) => set("aslang", v)} />} />
      </Group>

      <Group label="模型管理">
        <div className="models">
          {models.map(m => (
            <div className="model" key={m.id}>
              <div className="m-info">
                <div className="m-name">{m.name} <span className="m-tag mono">{m.tag}</span></div>
                {m.state === "downloading"
                  ? <div className="m-prog"><div className="bar"><i style={{ width: m.pct + "%" }} /></div><span className="mono">下载中 {m.pct}%</span></div>
                  : <div className="m-meta mono">{m.size} · {m.state === "done" ? <span className="ok-t">已下载</span> : <span className="muted">未下载</span>}</div>}
              </div>
              {m.state === "downloading"
                ? <button className="m-btn ghost">取消</button>
                : <button className={"m-btn" + (m.state === "done" ? " danger" : "")} onClick={() => act(m.id)}>
                    {m.state === "done" ? "删除" : "下载"}</button>}
            </div>
          ))}
        </div>
      </Group>
    </React.Fragment>
  );
}

function Permissions() {
  const [perms, setPerms] = useState({ mic: true, a11y: false, input: false });
  const [checking, setChecking] = useState(false);
  const recheck = () => {
    setChecking(true);
    setTimeout(() => { setPerms({ mic: true, a11y: true, input: false }); setChecking(false); }, 1100);
  };
  const items = [
    { k: "mic", title: "麦克风", help: "用于本地采集你的语音,音频不离开设备" },
    { k: "a11y", title: "辅助功能", help: "用于把识别出的文字插入到当前输入框" },
    { k: "input", title: "输入监控", help: "用于检测你按下的触发热键" },
  ];
  const allOk = perms.mic && perms.a11y && perms.input;
  return (
    <Group label="PERMISSIONS">
      {!allOk && <div className="banner warn">还有权限未开启,部分功能不可用。逐项「打开系统设置」后回到这里。</div>}
      {allOk && <div className="banner ok">全部权限已授权,Vibe IME 可以正常工作 ✓</div>}
      {items.map(it => (
        <Row key={it.k} title={it.title} help={it.help}
          control={
            <div className="perm-ctl">
              <StatusPill ok={perms[it.k]} />
              {!perms[it.k] && <button className="m-btn">打开系统设置</button>}
            </div>
          } />
      ))}
      <div className="recheck-row">
        <button className="m-btn ghost" onClick={recheck} disabled={checking}>
          {checking ? "检测中…" : "重新检测"}
        </button>
      </div>
    </Group>
  );
}

function About() {
  return (
    <Group>
      <div className="about">
        <div className="ab-logo"><div className="bars"><i></i><i></i><i></i><i></i><i></i></div></div>
        <div className="ab-name">Vibe IME</div>
        <div className="ab-ver mono">版本 1.0.0 (build 240) · macOS 13+ · Apple Silicon</div>
        <button className="m-btn">检查更新</button>
        <div className="ab-credits">
          <div className="cred-label mono">开源致谢</div>
          <div className="cred-list">
            {["FireRedVAD", "X-ASR", "sherpa-onnx", "silero-vad"].map(c => (
              <a key={c} className="cred" href="#">{c} ↗</a>
            ))}
          </div>
        </div>
        <div className="ab-foot mono">100% 本地 · 不联网 · 数据不出设备</div>
      </div>
    </Group>
  );
}

/* ---------- 主窗口 ---------- */
const TABS = [
  ["general", "通用", "⚙"],
  ["dictation", "听写", "🎙"],
  ["model", "模型", "🧠"],
  ["permissions", "权限", "🔐"],
  ["about", "关于", "ⓘ"],
];

function App() {
  const [tab, setTab] = useState("dictation");
  const [s, setS] = useState({
    autostart: true, glyph: "mono", theme: "auto", lang: "zh",
    combo: "Right ⌘", mode: "hold", insert: "paste", autopunct: true, eos: "whole",
    vad: "fire", latency: "480", provider: "coreml", aslang: "zh-en",
  });
  const set = (k, v) => setS(p => ({ ...p, [k]: v }));

  return (
    <div className="win">
      <div className="titlebar">
        <div className="lights"><span className="tl r"></span><span className="tl y"></span><span className="tl g"></span></div>
        <div className="win-title">偏好设置</div>
      </div>
      <div className="win-body">
        <aside className="sidebar">
          <div className="sb-brand">
            <div className="sb-logo"><div className="bars"><i></i><i></i><i></i></div></div>
            <span>Vibe IME</span>
          </div>
          {TABS.map(([id, label, icon]) => (
            <button key={id} className={"sb-item" + (tab === id ? " on" : "")} onClick={() => setTab(id)}>
              <span className="sb-ico">{icon}</span>{label}
            </button>
          ))}
        </aside>
        <main className="content">
          {tab === "general" && <General s={s} set={set} />}
          {tab === "dictation" && <Dictation s={s} set={set} />}
          {tab === "model" && <Model s={s} set={set} />}
          {tab === "permissions" && <Permissions />}
          {tab === "about" && <About />}
        </main>
      </div>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
