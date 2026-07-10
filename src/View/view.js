(function () {
  const graph = window.GRAPH;
  const bundleName = window.BUNDLE_NAME || "bundle";
  document.title = `${bundleName} — OKF Viewer`;
  document.getElementById("bundle-name").textContent = bundleName;

  if (!graph || !graph.nav) {
    document.getElementById("content-body").textContent =
      "This viewer requires a graph built with body and nav (okf view).";
    return;
  }

  const nodesById = {};
  for (const n of graph.nodes || []) nodesById[n.id] = n;

  const conceptIds = new Set(Object.keys(nodesById));
  const dirById = {};
  const dirIds = new Set();

  function indexDirs(node) {
    if (!node) return;
    if (node.kind === "dir") {
      dirById[node.id ?? ""] = node;
      dirIds.add(node.id ?? "");
    }
    for (const c of node.children || []) indexDirs(c);
  }
  indexDirs(graph.nav);

  const backlinks = {};
  for (const e of graph.edges || []) {
    (backlinks[e.target] ||= []).push(e.source);
  }

  /** @type {{ kind: 'dir'|'concept', id: string }} */
  let selected = { kind: "dir", id: "" };

  const treeEl = document.getElementById("tree");
  const searchEl = document.getElementById("nav-search");

  // ——— Link resolution (mirrors MarkdownLinks + NormalizeToConceptId) ———

  function isInternalLink(target) {
    if (!target || target[0] === "#") return false;
    const lower = target.toLowerCase();
    if (lower.startsWith("http://") || lower.startsWith("https://") || lower.startsWith("mailto:"))
      return false;
    if (lower.startsWith("javascript:") || lower.startsWith("data:")) return false;
    return true;
  }

  function dirname(path) {
    const i = path.lastIndexOf("/");
    return i <= 0 ? "" : path.slice(0, i);
  }

  function normalizePath(parts) {
    const out = [];
    for (const p of parts) {
      if (!p || p === ".") continue;
      if (p === "..") {
        if (out.length) out.pop();
        continue;
      }
      out.push(p);
    }
    return out.join("/");
  }

  function resolveHref(href, basePath) {
    let pathPart = href.split("#", 2)[0];
    if (!pathPart) return null;
    pathPart = pathPart.trim();

    let candidate;
    if (pathPart.startsWith("/")) {
      candidate = pathPart.replace(/^\/+/, "");
    } else {
      const baseDir = dirname(basePath);
      const joined = (baseDir ? baseDir + "/" : "") + pathPart;
      candidate = normalizePath(joined.split("/"));
    }

    if (pathPart.endsWith("/")) {
      candidate = (candidate ? candidate.replace(/\/?$/, "") + "/" : "") + "index.md";
      candidate = candidate.replace(/^\//, "");
    }

    return candidate.replace(/\\/g, "/");
  }

  function normalizeToId(resolved) {
    let id = resolved.replace(/\\/g, "/");
    if (id.toLowerCase().endsWith(".md")) id = id.slice(0, -3);
    if (id.toLowerCase().endsWith("/index")) id = id.slice(0, -6);
    id = id.replace(/\/+$/, "");
    return id;
  }

  function basePathForSelection() {
    if (selected.kind === "concept") {
      const n = nodesById[selected.id];
      return (n && n.path) || selected.id + ".md";
    }
    return selected.id === "" ? "index.md" : selected.id + "/index.md";
  }

  function tryNavigateHref(href) {
    if (href == null) return false;
    const raw = href.trim();
    if (raw.startsWith("#")) return false; // fragment-only: let browser/TOC handle
    if (!isInternalLink(raw)) return false;

    const resolved = resolveHref(raw, basePathForSelection());
    if (resolved == null) return false;
    const id = normalizeToId(resolved);

    if (conceptIds.has(id)) {
      select({ kind: "concept", id });
      return true;
    }
    if (dirIds.has(id)) {
      select({ kind: "dir", id });
      return true;
    }
    return false;
  }

  // ——— Tree ———

  function renderTree(filter) {
    treeEl.innerHTML = "";
    const q = (filter || "").trim().toLowerCase();
    const root = document.createElement("div");
    appendNavChildren(root, graph.nav.children || [], q, 0);
    // Also allow selecting the root dir
    const rootBtn = document.createElement("button");
    rootBtn.type = "button";
    rootBtn.className = "item" + (selected.kind === "dir" && selected.id === "" ? " active" : "");
    rootBtn.textContent = graph.nav.label || bundleName;
    rootBtn.dataset.kind = "dir";
    rootBtn.dataset.id = "";
    rootBtn.addEventListener("click", () => select({ kind: "dir", id: "" }));
    treeEl.appendChild(rootBtn);
    treeEl.appendChild(root);
  }

  function textMatch(node, q) {
    if (!q) return true;
    const hay = [
      node.label || "",
      node.id || "",
      node.description || "",
    ].join(" ").toLowerCase();
    if (hay.includes(q)) return true;
    if (node.kind === "concept" && nodesById[node.id]) {
      const n = nodesById[node.id];
      const t = ((n.tags || []).join(" ") + " " + (n.title || "")).toLowerCase();
      if (t.includes(q)) return true;
    }
    return false;
  }

  function subtreeMatches(node, q) {
    if (textMatch(node, q)) return true;
    return (node.children || []).some((c) => subtreeMatches(c, q));
  }

  function appendNavChildren(container, children, q, depth) {
    for (const node of children) {
      if (q && !subtreeMatches(node, q)) continue;

      if (node.kind === "group" || node.kind === "orphans") {
        const label = document.createElement("div");
        label.className = "group-label";
        label.textContent = node.label || (node.kind === "orphans" ? "Other" : "Group");
        container.appendChild(label);
        const wrap = document.createElement("div");
        wrap.className = "children";
        appendNavChildren(wrap, node.children || [], q, depth + 1);
        container.appendChild(wrap);
        continue;
      }

      if (node.kind === "dir") {
        const details = document.createElement("details");
        details.open = shouldExpandDir(node.id, q);
        const summary = document.createElement("summary");
        summary.textContent = node.label || node.id || "/";
        if (selected.kind === "dir" && selected.id === (node.id || "")) {
          summary.classList.add("active");
        }
        summary.addEventListener("click", (e) => {
          // allow toggle; also select on click
          select({ kind: "dir", id: node.id || "" });
        });
        details.appendChild(summary);
        const kids = document.createElement("div");
        kids.className = "children";
        appendNavChildren(kids, node.children || [], q, depth + 1);
        details.appendChild(kids);
        container.appendChild(details);
        continue;
      }

      if (node.kind === "concept") {
        const btn = document.createElement("button");
        btn.type = "button";
        btn.className = "item" + (selected.kind === "concept" && selected.id === node.id ? " active" : "");
        btn.textContent = node.label || node.id;
        btn.title = node.description || node.id;
        btn.addEventListener("click", () => select({ kind: "concept", id: node.id }));
        container.appendChild(btn);
      }
    }
  }

  function shouldExpandDir(dirId, q) {
    if (q) return true;
    if (selected.kind === "dir" && (selected.id === dirId || selected.id.startsWith(dirId + "/")))
      return true;
    if (selected.kind === "concept" && selected.id.startsWith((dirId ? dirId + "/" : "")))
      return true;
    // expand first level by default
    return !dirId || !dirId.includes("/");
  }

  // ——— Content ———

  function select(next) {
    selected = next;
    renderTree(searchEl.value);
    renderContent();
  }

  function renderContent() {
    const titleEl = document.getElementById("content-title");
    const descEl = document.getElementById("content-desc");
    const typeEl = document.getElementById("content-type");
    const metaEl = document.getElementById("content-meta");
    const bodyEl = document.getElementById("content-body");
    const blSection = document.getElementById("backlinks");
    const blList = document.getElementById("backlinks-list");

    metaEl.innerHTML = "";
    blList.innerHTML = "";

    let md = "";
    if (selected.kind === "dir") {
      const d = dirById[selected.id] || graph.nav;
      titleEl.textContent = d.label || selected.id || bundleName;
      descEl.textContent = d.description || (d.synthetic ? "Synthetic directory listing" : "");
      typeEl.hidden = true;
      md = d.body || "";
      blSection.hidden = true;
    } else {
      const n = nodesById[selected.id];
      if (!n) {
        titleEl.textContent = "Not found";
        bodyEl.textContent = "";
        return;
      }
      titleEl.textContent = n.title || n.label || n.id;
      descEl.textContent = n.description || "";
      typeEl.hidden = false;
      typeEl.textContent = n.type || "Concept";
      if (n.tags && n.tags.length) {
        for (const t of n.tags) {
          const span = document.createElement("span");
          span.className = "tag";
          span.textContent = t;
          metaEl.appendChild(span);
        }
      }
      if (n.resource) {
        const a = document.createElement("a");
        a.href = n.resource;
        a.textContent = n.resource;
        a.target = "_blank";
        a.rel = "noopener";
        a.className = "external";
        metaEl.appendChild(a);
      }
      md = n.body || "";

      const bl = backlinks[selected.id] || [];
      if (bl.length) {
        blSection.hidden = false;
        for (const src of bl) {
          const li = document.createElement("li");
          const a = document.createElement("a");
          a.textContent = nodesById[src]?.title || nodesById[src]?.label || src;
          a.addEventListener("click", (e) => {
            e.preventDefault();
            select({ kind: "concept", id: src });
          });
          li.appendChild(a);
          blList.appendChild(li);
        }
      } else {
        blSection.hidden = true;
      }
    }

    const rawHtml = marked.parse(md || "", { breaks: false, gfm: true });
    const clean = DOMPurify.sanitize(rawHtml, { USE_PROFILES: { html: true } });
    bodyEl.innerHTML = clean;
    slugifyHeadings(bodyEl);
    wireContentLinks(bodyEl);
    buildToc(bodyEl);
  }

  function slugifyHeadings(root) {
    const used = new Set();
    root.querySelectorAll("h1, h2, h3").forEach((h) => {
      let base = (h.textContent || "section")
        .toLowerCase()
        .replace(/[^\w\s-]/g, "")
        .trim()
        .replace(/\s+/g, "-");
      if (!base) base = "section";
      let id = base;
      let i = 2;
      while (used.has(id)) {
        id = base + "-" + i++;
      }
      used.add(id);
      h.id = id;
    });
  }

  function wireContentLinks(root) {
    root.querySelectorAll("a[href]").forEach((a) => {
      const href = a.getAttribute("href");
      if (!href) return;

      if (href.startsWith("#")) {
        a.addEventListener("click", (e) => {
          const id = href.slice(1);
          const el = document.getElementById(id);
          if (el) {
            e.preventDefault();
            el.scrollIntoView({ behavior: "smooth", block: "start" });
          }
        });
        return;
      }

      if (!isInternalLink(href)) {
        a.classList.add("external");
        a.setAttribute("target", "_blank");
        a.setAttribute("rel", "noopener");
        if (href.toLowerCase().startsWith("javascript:")) {
          a.addEventListener("click", (e) => e.preventDefault());
          a.removeAttribute("href");
        }
        return;
      }

      a.classList.add("internal");
      a.addEventListener("click", (e) => {
        if (tryNavigateHref(href)) {
          e.preventDefault();
        }
      });
    });
  }

  function buildToc(root) {
    const list = document.getElementById("toc-list");
    list.innerHTML = "";
    root.querySelectorAll("h1, h2, h3").forEach((h) => {
      const li = document.createElement("li");
      const level = h.tagName === "H1" ? 1 : h.tagName === "H2" ? 2 : 3;
      li.className = "lvl-" + level;
      const a = document.createElement("a");
      a.href = "#" + h.id;
      a.textContent = h.textContent || h.id;
      a.addEventListener("click", (e) => {
        e.preventDefault();
        h.scrollIntoView({ behavior: "smooth", block: "start" });
      });
      li.appendChild(a);
      list.appendChild(li);
    });
  }

  searchEl.addEventListener("input", () => renderTree(searchEl.value));

  // Expose for tests / PR3
  window.__okfView = {
    select,
    tryNavigateHref,
    resolveHref,
    normalizeToId,
    get selected() {
      return selected;
    },
    basePathForSelection,
  };

  renderTree("");
  select({ kind: "dir", id: "" });
})();
