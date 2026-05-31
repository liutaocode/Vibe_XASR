/* ============================================================
   Vibe IME — Dictation HUD prototype
   按住说话 → 波形随声起伏 → partial→final 流式出字 → 松开落字
   Esc 取消丢弃。6 状态 × 3 形态 × 深浅。
   ============================================================ */
const { useState, useRef, useEffect, useCallback } = React;

/* ---------- 示例例句(code-switch),分段=句间停顿 ---------- */
const SCRIPTS = {
  zh: [
    { segs: ["帮我把这个 function 改成 async，", "然后加上 error handling"] },
    { segs: ["commit 一下，message 写 ", "fix: 修复登录态丢失"] },
    { segs: ["这个 component 再抽一个 hook 出来，", "叫 useAuth"] },
    { segs: ["跑一下 test，", "看 coverage 够不够"] },
    { segs: ["今天天气怎么样 by the way，", "提醒我下午三点开会"] },
  ],
  en: [
    { segs: ["change this function to async, ", "then add error handling"] },
    { segs: ["commit with message ", "fix: restore lost login state"] },
    { segs: ["extract a hook from this component, ", "call it useAuth"] },
  ],
};

const ERRORS = [
  { icon: "🎙", title: "找不到麦克风", reason: "请插入或选择一个输入设备" },
  { icon: "🔒", title: "未授权麦克风", reason: "需要在系统设置里允许 Vibe IME 访问麦克风" },
  { icon: "📈", title: "声音太大 · 爆音", reason: "输入电平削波,请降低音量或离麦远一点" },
];

/* ---------- 波形:ref 驱动的 60fps 振幅跟随 ---------- */
function Waveform({ phaseRef, ampRef, bars, big }) {
  const wrapRef = useRef(null);
  const barEls = useRef([]);
  const smooth = useRef([]);
  useEffect(() => {
    let raf;
    const N = bars;
    smooth.current = new Array(N).fill(0.1);
    const loop = () => {
      const phase = phaseRef.current;
      const amp = ampRef.current;
      for (let i = 0; i < N; i++) {
        const el = barEls.current[i];
        if (!el) continue;
        // center-weighted envelope so middle bars are tallest
        const c = 1 - Math.abs(i - (N - 1) / 2) / ((N - 1) / 2);
        const env = 0.35 + 0.65 * c;
        let target;
        if (phase === "speaking") {
          target = amp * env * (0.45 + Math.random() * 0.75);
        } else if (phase === "pause" || phase === "empty") {
          target = 0.06 + Math.random() * 0.03; // 压平成细线
        } else {
          target = 0.05;
        }
        // ease toward target
        smooth.current[i] += (target - smooth.current[i]) * 0.35;
        const h = Math.max(0.05, Math.min(1, smooth.current[i]));
        el.style.transform = `scaleY(${h})`;
        el.style.opacity = phase === "finalizing" || phase === "done" || phase === "cancel" ? 0 : 1;
      }
      raf = requestAnimationFrame(loop);
    };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, [bars]);

  return (
    <div ref={wrapRef} className={"wf" + (big ? " wf-big" : "")}>
      {Array.from({ length: bars }).map((_, i) => (
        <i key={i} ref={(el) => (barEls.current[i] = el)} />
      ))}
    </div>
  );
}

/* ---------- 状态光球 / ✓ / 错误 ---------- */
function Orb({ phase, error, big }) {
  let cls = "orb";
  if (phase === "finalizing" || phase === "done") cls += " orb-done";
  if (phase === "cancel") cls += " orb-cancel";
  if (phase === "error") cls += " orb-error";
  if (phase === "speaking") cls += " orb-live";
  return (
    <div className={cls + (big ? " orb-big" : "")}>
      {(phase === "finalizing" || phase === "done") && <span className="glyph">✓</span>}
      {phase === "cancel" && <span className="glyph">✕</span>}
      {phase === "error" && <span className="glyph">{error?.icon || "!"}</span>}
      {(phase === "empty" || phase === "speaking" || phase === "pause" || phase === "idle") && (
        <span className="pulse" />
      )}
    </div>
  );
}

/* ---------- HUD 本体 ---------- */
function Hud({ form, phase, text, error, elapsed, phaseRef, ampRef, onCopy, copied, onHoverChange }) {
  const visible = phase !== "idle";
  const expanded = form === "expanded";
  const radical = form === "radical";
  const bars = radical ? 36 : expanded ? 30 : 24;

  let statusRight = null;
  if (phase === "speaking" || phase === "pause" || phase === "empty") {
    statusRight = <span className="timer mono">{elapsed}</span>;
  } else if (phase === "finalizing" || phase === "done") {
    statusRight = <span className="ok">已插入</span>;
  } else if (phase === "cancel") {
    statusRight = <span className="cancel-t">已取消</span>;
  }

  const showText =
    phase === "empty" ? "" : text;
  const placeholder = phase === "empty" ? "在听…" : phase === "error" ? "" : "";

  return (
    <div
      className={
        "hud-anchor" + (radical ? " anchor-radical" : "")
      }
    >
      <div
        className={
          "hud hud-" + form +
          (visible ? " in" : " out") +
          " phase-" + phase
        }
        onMouseEnter={() => onHoverChange(true)}
        onMouseLeave={() => onHoverChange(false)}
        aria-live="polite"
      >
        {radical ? (
          <React.Fragment>
            <div className="rad-orb">
              <Orb phase={phase} error={error} big />
              <div className="rad-ring"><Waveform phaseRef={phaseRef} ampRef={ampRef} bars={bars} big /></div>
            </div>
            <div className="rad-text mono">
              {phase === "error" ? (
                <div className="err-block">
                  <div className="err-title">{error?.title}</div>
                  <div className="err-reason">{error?.reason}</div>
                </div>
              ) : (
                <React.Fragment>
                  <span className="t-content">{showText || placeholder}</span>
                  {(phase === "speaking") && <span className="caret">▌</span>}
                  {phase === "pause" && <span className="dots">…</span>}
                </React.Fragment>
              )}
            </div>
            {phase !== "error" && <div className="rad-status">{statusRight}</div>}
          </React.Fragment>
        ) : (
          <React.Fragment>
            <div className="hud-left">
              {phase === "error" ? (
                <Orb phase={phase} error={error} />
              ) : (
                <div className="orb-wrap">
                  <Orb phase={phase} error={error} />
                  {(phase === "speaking" || phase === "pause" || phase === "empty") && (
                    <div className="orb-wf"><Waveform phaseRef={phaseRef} ampRef={ampRef} bars={bars} /></div>
                  )}
                </div>
              )}
            </div>

            <div className={"hud-mid mono" + (expanded ? " mid-exp" : "")}>
              {phase === "error" ? (
                <div className="err-block">
                  <div className="err-title">{error?.title}</div>
                  <div className="err-reason">{error?.reason}</div>
                </div>
              ) : (
                <div className="line">
                  <span className="t-content">{showText || placeholder}</span>
                  {phase === "speaking" && <span className="caret">▌</span>}
                  {phase === "pause" && <span className="dots">…</span>}
                </div>
              )}
              {expanded && phase !== "error" && showText && (
                <div className="exp-bar">
                  <button className="exp-btn" onClick={onCopy}>{copied ? "已复制 ✓" : "复制全文"}</button>
                  <span className="exp-hint mono">{phase === "done" ? "已插入到光标处" : "松开落字 · Esc 取消"}</span>
                </div>
              )}
            </div>

            <div className="hud-right">
              {phase === "error" ? (
                <button className="goset">去设置</button>
              ) : (
                statusRight
              )}
            </div>
          </React.Fragment>
        )}
      </div>
    </div>
  );
}

/* ---------- 主程序:桌面 + HUD + 控制条 ---------- */
function App() {
  const [theme, setTheme] = useState(localStorage.getItem("vibe-theme") || "dark");
  const [form, setForm] = useState("compact");
  const [lang, setLang] = useState("zh");
  const [phase, setPhase] = useState("idle");
  const [text, setText] = useState("");
  const [error, setError] = useState(null);
  const [elapsed, setElapsed] = useState("0:00");
  const [editor, setEditor] = useState("");
  const [copied, setCopied] = useState(false);
  const [holding, setHolding] = useState(false);

  const phaseRef = useRef("idle");
  const ampRef = useRef(0);
  const timers = useRef([]);
  const hoverRef = useRef(false);
  const elapsedStart = useRef(0);
  const scriptIdx = useRef(0);
  const fullTextRef = useRef("");

  const setPhaseBoth = (p) => { phaseRef.current = p; setPhase(p); };
  const clearTimers = () => { timers.current.forEach(clearTimeout); timers.current = []; };
  const after = (ms, fn) => { const id = setTimeout(fn, ms); timers.current.push(id); return id; };

  useEffect(() => {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem("vibe-theme", theme);
  }, [theme]);

  // elapsed timer
  useEffect(() => {
    if (!(phase === "speaking" || phase === "pause" || phase === "empty")) return;
    const id = setInterval(() => {
      const s = Math.floor((Date.now() - elapsedStart.current) / 1000);
      setElapsed(`0:${String(s).padStart(2, "0")}`);
    }, 250);
    return () => clearInterval(id);
  }, [phase]);

  /* ---- streaming reveal ---- */
  const revealSegment = (segText, base, done) => {
    let i = 0;
    const step = () => {
      i++;
      setText(base + segText.slice(0, i));
      if (i < segText.length) {
        const ch = segText[i - 1];
        const d = /[A-Za-z]/.test(ch) ? 28 : 58 + Math.random() * 40;
        after(d, step);
      } else {
        done(base + segText);
      }
    };
    step();
  };

  const startDictation = () => {
    if (phase !== "idle" && phase !== "done" && phase !== "cancel" && phase !== "error") return;
    clearTimers();
    setCopied(false);
    setText("");
    setError(null);
    ampRef.current = 0;
    elapsedStart.current = Date.now();
    setElapsed("0:00");
    setPhaseBoth("empty");
    setHolding(true);

    const script = SCRIPTS[lang][scriptIdx.current % SCRIPTS[lang].length];
    scriptIdx.current++;
    fullTextRef.current = script.segs.join("");

    // 占位 ~450ms 后开始出声
    after(450, () => {
      if (phaseRef.current !== "empty") return;
      ampRef.current = 1;
      setPhaseBoth("speaking");
      revealSegment(script.segs[0], "", (acc) => {
        // 句间停顿
        ampRef.current = 0.08;
        setPhaseBoth("pause");
        after(700, () => {
          if (phaseRef.current !== "pause") return;
          ampRef.current = 1;
          setPhaseBoth("speaking");
          revealSegment(script.segs[1], acc, () => {
            // 说完仍按住 → 等待(细线)
            ampRef.current = 0.08;
            setPhaseBoth("pause");
          });
        });
      });
    });
  };

  const stopDictation = () => {
    if (!holding) return;
    setHolding(false);
    if (phaseRef.current === "idle" || phaseRef.current === "cancel" || phaseRef.current === "error") return;
    clearTimers();
    const finalText = fullTextRef.current;
    setText(finalText);
    ampRef.current = 0;
    setPhaseBoth("finalizing");
    // 轻回弹后插入
    after(360, () => {
      setEditor((e) => e + (e && !e.endsWith("\n") ? " " : "") + finalText);
      setPhaseBoth("done");
      // 0.6s 后淡出(若悬停则保持)
      const tryHide = () => {
        if (hoverRef.current) { after(300, tryHide); return; }
        setPhaseBoth("idle");
        setText("");
      };
      after(600, tryHide);
    });
  };

  const cancelDictation = () => {
    if (phaseRef.current === "idle") return;
    clearTimers();
    setHolding(false);
    ampRef.current = 0;
    setPhaseBoth("cancel");
    after(750, () => { setPhaseBoth("idle"); setText(""); });
  };

  const triggerError = () => {
    clearTimers();
    setHolding(false);
    const e = ERRORS[Math.floor(Math.random() * ERRORS.length)];
    setError(e);
    ampRef.current = 0;
    setPhaseBoth("error");
    after(2600, () => { if (phaseRef.current === "error") { setPhaseBoth("idle"); setError(null); } });
  };

  const copyText = () => {
    navigator.clipboard?.writeText(fullTextRef.current).catch(() => {});
    setCopied(true);
    after(1400, () => setCopied(false));
  };

  /* ---- keyboard: hold Space = push-to-talk, Esc = cancel ---- */
  useEffect(() => {
    const down = (e) => {
      if (e.code === "Space" && !e.repeat) {
        e.preventDefault();
        startDictation();
      } else if (e.key === "Escape") {
        cancelDictation();
      }
    };
    const up = (e) => {
      if (e.code === "Space") { e.preventDefault(); stopDictation(); }
    };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup", up);
    return () => { window.removeEventListener("keydown", down); window.removeEventListener("keyup", up); };
  });

  return (
    <div className="stage">
      <Desktop editor={editor} phase={phase} />

      <Hud
        form={form} phase={phase} text={text} error={error} elapsed={elapsed}
        phaseRef={phaseRef} ampRef={ampRef} onCopy={copyText} copied={copied}
        onHoverChange={(h) => (hoverRef.current = h)}
      />

      {/* ---- 控制条 ---- */}
      <div className="controls">
        <div className="ctl-group">
          <button
            className={"ptt" + (holding ? " ptt-on" : "")}
            onPointerDown={(e) => { e.preventDefault(); startDictation(); }}
            onPointerUp={(e) => { e.preventDefault(); stopDictation(); }}
            onPointerLeave={() => { if (holding) stopDictation(); }}
          >
            <span className="ptt-dot" />
            {holding ? "松开落字" : "按住说话"}
          </button>
          <span className="ctl-hint mono">或按住 <kbd>Space</kbd> · <kbd>Esc</kbd> 取消</span>
        </div>

        <div className="ctl-sep" />

        <div className="seg" role="group" aria-label="形态">
          {[["compact","紧凑"],["expanded","展开"],["radical","激进"]].map(([v,l]) => (
            <button key={v} className={form===v?"on":""} onClick={() => setForm(v)}>{l}</button>
          ))}
        </div>

        <div className="seg" role="group" aria-label="语言">
          {[["zh","中"],["en","EN"]].map(([v,l]) => (
            <button key={v} className={lang===v?"on":""} onClick={() => setLang(v)}>{l}</button>
          ))}
        </div>

        <div className="seg" role="group" aria-label="主题">
          {[["dark","深"],["light","浅"]].map(([v,l]) => (
            <button key={v} className={theme===v?"on":""} onClick={() => setTheme(v)}>{l}</button>
          ))}
        </div>

        <div className="ctl-sep" />

        <button className="err-btn" onClick={triggerError}>模拟错误</button>
      </div>
    </div>
  );
}

/* ---------- 仿 macOS 桌面 ---------- */
function Desktop({ editor, phase }) {
  const listening = phase === "speaking" || phase === "pause" || phase === "empty";
  const glyph = listening ? "🔴" : (phase === "finalizing" || phase === "done") ? "✍️" : "🎙";
  return (
    <div className="desktop">
      <div className="menubar">
        <div className="mb-left">
          <span className="apple"></span>
          <strong>Cursor</strong>
          <span>文件</span><span>编辑</span><span>选择</span><span>查看</span><span>运行</span>
        </div>
        <div className="mb-right">
          <span className={"mb-glyph" + (listening ? " live" : "")}>{glyph}</span>
          <span className="mono">100%</span>
          <span className="mono">周日 23:08</span>
        </div>
      </div>

      <div className="editor-win">
        <div className="ew-bar">
          <span className="tl r"></span><span className="tl y"></span><span className="tl g"></span>
          <span className="ew-title mono">app/auth/useAuth.ts — Cursor</span>
        </div>
        <div className="ew-body">
          <div className="gutter mono">{Array.from({length:7}).map((_,i)=><div key={i}>{i+1}</div>)}</div>
          <div className="code mono">
            <div><span className="kw">import</span> {"{ useState }"} <span className="kw">from</span> <span className="str">'react'</span></div>
            <div className="blank">&nbsp;</div>
            <div className="cmt">// 在这里对 Cursor 说话,文字落到光标处 ↓</div>
            <div className="prompt-line">
              <span className="t">{editor || ""}</span>
              <span className={"cursor" + (phase==="done"?" flash":"")}>▌</span>
            </div>
            <div className="blank">&nbsp;</div>
            <div className="cmt">// 100% 本地 · partial→final · 中英混说</div>
          </div>
        </div>
      </div>

      <div className="dock">
        {["#7C5CFF","#FF5C66","#45D483","#FFB020","#38E1D6","#8A8A99"].map((c,i)=>(
          <div key={i} className="dock-ico" style={{background:`linear-gradient(160deg, ${c}, ${c}cc)`}} />
        ))}
      </div>
    </div>
  );
}

ReactDOM.createRoot(document.getElementById("root")).render(<App />);
