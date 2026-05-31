// Claude Enterprise Console — vanilla ES module client.
// Talks to the ASP.NET Core API on the same origin.

const API = {
  send:    "/api/chat/messages",
  stream:  "/api/chat/messages/stream",
  list:    "/api/chat/conversations",
  get:     (id) => `/api/chat/conversations/${id}`,
  delete:  (id) => `/api/chat/conversations/${id}`,
  models:  "/api/models",
  usage:   "/api/usage",
  health:  "/health/ready",
};

const LS_KEY = "claude-enterprise.apiKey";
const getApiKey = () => localStorage.getItem(LS_KEY) || "";
const setApiKey = (v) => v ? localStorage.setItem(LS_KEY, v) : localStorage.removeItem(LS_KEY);
function authHeaders(extra = {}) {
  const k = getApiKey();
  return k ? { ...extra, "X-API-Key": k } : extra;
}

// ── State ────────────────────────────────────────────────────────────────────
const state = {
  conversationId: null,
  conversations: [],
  models: [],
  busy: false,
  controller: null,
};

// ── DOM ──────────────────────────────────────────────────────────────────────
const $ = (sel) => document.querySelector(sel);
const els = {
  messages:   $("#messages"),
  composer:   $("#composer"),
  input:      $("#input"),
  send:       $("#send-btn"),
  stop:       $("#stop-btn"),
  newChat:    $("#new-chat"),
  list:       $("#conversations"),
  title:      $("#conversation-title"),
  modelBadge: $("#model-badge"),
  health:     $("#health-status"),
  tpl:        $("#msg-template"),
  setKey:     $("#set-key"),
  showUsage:  $("#show-usage"),
  opt: {
    model:  $("#opt-model"),
    max:    $("#opt-max"),
    temp:   $("#opt-temp"),
    system: $("#opt-system"),
    stream: $("#opt-stream"),
  },
  modelHint: $("#model-hint"),
};

// ── Helpers ──────────────────────────────────────────────────────────────────
const fmt = new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit" });
const escape = (s) => s.replace(/[&<>"']/g, (c) => ({ "&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;" }[c]));

function renderMarkdownLite(text) {
  // very small, safe transform: code fences + inline code + line breaks
  const escaped = escape(text);
  return escaped
    .replace(/```([\s\S]*?)```/g, (_, code) => `<pre><code>${code}</code></pre>`)
    .replace(/`([^`\n]+)`/g, "<code>$1</code>");
}

function addMessageNode(role, content, { streaming = false } = {}) {
  const empty = els.messages.querySelector(".empty");
  if (empty) empty.remove();

  const node = els.tpl.content.firstElementChild.cloneNode(true);
  node.classList.add(role);
  node.querySelector(".avatar").textContent = role === "user" ? "U" : role === "assistant" ? "C" : "!";
  node.querySelector(".role").textContent = role === "user" ? "You" : role === "assistant" ? "Claude" : "Error";
  node.querySelector(".ts").textContent = fmt.format(new Date());
  const contentEl = node.querySelector(".content");
  contentEl.innerHTML = renderMarkdownLite(content);
  if (streaming) contentEl.classList.add("cursor");
  els.messages.appendChild(node);
  els.messages.scrollTop = els.messages.scrollHeight;
  return node;
}

function addMessage(role, content, opts = {}) {
  return addMessageNode(role, content, opts).querySelector(".content");
}

function showEmpty() {
  els.messages.innerHTML = `
    <div class="empty">
      <h2>How can I help today?</h2>
      <p>Start a new conversation by typing below.</p>
    </div>`;
}

function setBusy(b) {
  state.busy = b;
  els.send.classList.toggle("loading", b);
  els.send.disabled = b;
  els.input.disabled = b;
  els.stop.hidden = !b || !state.controller;
}

function buildRequest(message) {
  const sys = els.opt.system.value.trim();
  return {
    conversationId: state.conversationId,
    message,
    system: sys || null,
    model: els.opt.model.value || null,
    maxTokens: Number(els.opt.max.value) || null,
    temperature: Number(els.opt.temp.value),
  };
}

// ── Models ───────────────────────────────────────────────────────────────────
async function loadModels() {
  try {
    const res = await fetch(API.models, { headers: authHeaders() });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    state.models = await res.json();
  } catch (err) {
    console.warn("Model catalog unavailable:", err);
    state.models = [];
  }
  renderModels();
}

function renderModels() {
  const select = els.opt.model;
  select.innerHTML = "";
  if (!state.models.length) {
    const opt = document.createElement("option");
    opt.value = "";
    opt.textContent = "Default (server-side)";
    select.appendChild(opt);
    updateModelHint();
    return;
  }

  // Group by family
  const families = {};
  for (const m of state.models) (families[m.family] ||= []).push(m);

  for (const [family, items] of Object.entries(families)) {
    const group = document.createElement("optgroup");
    group.label = family;
    for (const m of items) {
      const opt = document.createElement("option");
      opt.value = m.id;
      opt.dataset.tier = m.tier;
      opt.dataset.description = m.description;
      opt.dataset.context = m.contextWindow;
      opt.dataset.maxOutput = m.maxOutputTokens;
      opt.textContent = m.recommended ? `${m.displayName} ★` : m.displayName;
      if (m.isDefault) opt.selected = true;
      group.appendChild(opt);
    }
    select.appendChild(group);
  }
  els.modelBadge.textContent = select.value;
  updateModelHint();
}

function updateModelHint() {
  const opt = els.opt.model.selectedOptions[0];
  if (!opt || !opt.dataset.description) {
    els.modelHint.textContent = "";
    return;
  }
  const ctx = Number(opt.dataset.context).toLocaleString();
  const out = Number(opt.dataset.maxOutput).toLocaleString();
  els.modelHint.textContent = `${opt.dataset.tier} · ${ctx} ctx · ${out} max out — ${opt.dataset.description}`;
}

// ── API ──────────────────────────────────────────────────────────────────────
async function send(message) {
  const body = buildRequest(message);
  if (els.opt.stream.checked) return sendStreaming(body);

  const res = await fetch(API.send, {
    method: "POST",
    headers: authHeaders({ "content-type": "application/json" }),
    body: JSON.stringify(body),
  });
  if (!res.ok) throw new Error((await res.json().catch(() => ({}))).detail || res.statusText);
  const data = await res.json();
  state.conversationId = data.conversationId;
  addMessage("assistant", data.reply);
  await refreshConversations();
}

async function sendStreaming(body) {
  state.controller = new AbortController();
  els.stop.hidden = false;
  const article = addMessageNode("assistant", "", { streaming: true });
  const contentEl = article.querySelector(".content");
  let buffer = "";
  let streamError = null;

  try {
    const res = await fetch(API.stream, {
      method: "POST",
      headers: authHeaders({ "content-type": "application/json" }),
      body: JSON.stringify(body),
      signal: state.controller.signal,
    });
    if (!res.ok || !res.body) throw new Error(`HTTP ${res.status}`);

    const reader = res.body.getReader();
    const decoder = new TextDecoder();
    let raw = "";

    while (true) {
      const { value, done } = await reader.read();
      if (done) break;
      raw += decoder.decode(value, { stream: true });

      let idx;
      while ((idx = raw.indexOf("\n\n")) !== -1) {
        const frame = raw.slice(0, idx);
        raw = raw.slice(idx + 2);
        const dataLine = frame.split("\n").find((l) => l.startsWith("data:"));
        if (!dataLine) continue;
        const payload = JSON.parse(dataLine.slice(5).trim());
        if (payload.conversationId) state.conversationId = payload.conversationId;

        if (payload.type === "delta" && payload.delta) {
          buffer += payload.delta;
          contentEl.innerHTML = renderMarkdownLite(buffer);
          els.messages.scrollTop = els.messages.scrollHeight;
        } else if (payload.type === "error") {
          streamError = payload.error || "stream error";
        }
      }
    }
  } finally {
    contentEl.classList.remove("cursor");
    state.controller = null;
    els.stop.hidden = true;
  }

  if (streamError) {
    // Replace empty assistant bubble with a clear error bubble
    if (!buffer) article.remove();
    addMessage("error", streamError);
    throw new Error(streamError);
  }
  await refreshConversations();
}

async function refreshConversations() {
  try {
    const res = await fetch(API.list, { headers: authHeaders() });
    if (!res.ok) return;
    state.conversations = await res.json();
    renderConversations();
  } catch { /* ignore */ }
}

async function loadConversation(id) {
  const res = await fetch(API.get(id), { headers: authHeaders() });
  if (!res.ok) return;
  const conv = await res.json();
  state.conversationId = conv.id;
  els.title.textContent = conv.title || "Conversation";
  els.messages.innerHTML = "";
  for (const m of conv.messages) {
    addMessage(m.role.toLowerCase(), m.content);
  }
  if (!conv.messages.length) showEmpty();
  renderConversations();
}

async function deleteConversation(id) {
  await fetch(API.delete(id), { method: "DELETE", headers: authHeaders() });
  if (state.conversationId === id) startNewChat();
  await refreshConversations();
}

async function checkHealth() {
  try {
    const r = await fetch(API.health);
    const ok = r.ok;
    els.health.classList.toggle("ok", ok);
    els.health.classList.toggle("err", !ok);
    els.health.querySelector(".label").textContent = ok ? "API ready" : "API not ready";
  } catch {
    els.health.classList.add("err");
    els.health.querySelector(".label").textContent = "API unreachable";
  }
}

async function showUsage() {
  try {
    const r = await fetch(API.usage, { headers: authHeaders() });
    if (!r.ok) { alert(`Usage unavailable (HTTP ${r.status}).`); return; }
    const u = await r.json();
    const rows = u.models.map(m =>
      `${m.model.padEnd(28)} in=${m.inputTokens.toString().padStart(8)}  out=${m.outputTokens.toString().padStart(8)}  reqs=${m.requests.toString().padStart(4)}  $${m.estimatedCostUsd}`
    ).join("\n");
    alert(
      `Usage since ${new Date(u.since).toLocaleString()}\n` +
      `Total in:  ${u.totalInputTokens}\n` +
      `Total out: ${u.totalOutputTokens}\n` +
      `Est. cost: $${u.estimatedCostUsd}\n\n` +
      (rows || "(no calls yet)")
    );
  } catch (e) {
    alert("Usage error: " + e.message);
  }
}

// ── Rendering ────────────────────────────────────────────────────────────────
function renderConversations() {
  els.list.innerHTML = "";
  for (const c of state.conversations) {
    const btn = document.createElement("button");
    btn.className = "conv-item" + (c.id === state.conversationId ? " active" : "");
    btn.innerHTML = `<span class="ttl">${escape(c.title || "Untitled")}</span>
                     <span class="del" title="Delete" aria-label="Delete">×</span>`;
    btn.addEventListener("click", (e) => {
      if (e.target.classList.contains("del")) {
        e.stopPropagation();
        deleteConversation(c.id);
      } else {
        loadConversation(c.id);
      }
    });
    els.list.appendChild(btn);
  }
}

function startNewChat() {
  state.conversationId = null;
  els.title.textContent = "New conversation";
  showEmpty();
  renderConversations();
  els.input.focus();
}

// ── Events ───────────────────────────────────────────────────────────────────
els.composer.addEventListener("submit", async (e) => {
  e.preventDefault();
  const text = els.input.value.trim();
  if (!text || state.busy) return;
  addMessage("user", text);
  els.input.value = "";
  els.input.style.height = "auto";
  setBusy(true);
  try {
    await send(text);
  } catch (err) {
    addMessage("error", err.message || "Request failed.");
  } finally {
    setBusy(false);
    els.input.focus();
  }
});

els.input.addEventListener("keydown", (e) => {
  if (e.key === "Enter" && !e.shiftKey) {
    e.preventDefault();
    els.composer.requestSubmit();
  }
});

els.input.addEventListener("input", () => {
  els.input.style.height = "auto";
  els.input.style.height = Math.min(200, els.input.scrollHeight) + "px";
});

document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && state.controller) state.controller.abort();
});

els.stop.addEventListener("click", () => state.controller?.abort());

els.setKey.addEventListener("click", (e) => {
  e.preventDefault();
  const current = getApiKey();
  const next = prompt("X-API-Key (leave blank to clear):", current);
  if (next === null) return;
  setApiKey(next.trim());
  checkHealth();
  refreshConversations();
  loadModels();
});

els.showUsage.addEventListener("click", (e) => {
  e.preventDefault();
  showUsage();
});

els.newChat.addEventListener("click", startNewChat);

els.opt.model.addEventListener("change", () => {
  els.modelBadge.textContent = els.opt.model.value || "default";
  const opt = els.opt.model.selectedOptions[0];
  if (opt?.dataset.maxOutput) {
    const cap = Number(opt.dataset.maxOutput);
    els.opt.max.max = cap;
    if (Number(els.opt.max.value) > cap) els.opt.max.value = cap;
  }
  updateModelHint();
});

// ── Init ─────────────────────────────────────────────────────────────────────
showEmpty();
loadModels();
refreshConversations();
checkHealth();
setInterval(checkHealth, 30_000);
els.input.focus();
