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

  function select(next, opts) {
    selected = next;
    if (!(opts && opts.skipHash)) updateHash();
    renderTree(searchEl.value);
    renderContent();
    updateLocalGraph();
  }

  // ——— Hash routing (#c/id, #d/id) ———

  function updateHash() {
    try {
      const prefix = selected.kind === "dir" ? "d/" : "c/";
      const id = selected.id === "" && selected.kind === "dir" ? "" : selected.id;
      const hash = "#" + prefix + encodeURIComponent(id).replace(/%2F/gi, "/");
      if (location.hash !== hash) history.replaceState(null, "", hash);
    } catch (_) {}
  }

  function selectFromHash() {
    const h = location.hash || "";
    if (!h || h === "#") return false;
    let raw;
    try {
      raw = decodeURIComponent(h.slice(1));
    } catch (_) {
      raw = h.slice(1);
    }
    if (raw.startsWith("c/")) {
      const id = raw.slice(2);
      if (conceptIds.has(id)) {
        select({ kind: "concept", id }, { skipHash: true });
        return true;
      }
      // slug fallback
      for (const n of graph.nodes || []) {
        if (n.slug === id) {
          select({ kind: "concept", id: n.id }, { skipHash: true });
          return true;
        }
      }
    } else if (raw.startsWith("d/")) {
      const id = raw.slice(2);
      if (dirIds.has(id)) {
        select({ kind: "dir", id }, { skipHash: true });
        return true;
      }
    } else if (conceptIds.has(raw)) {
      select({ kind: "concept", id: raw }, { skipHash: true });
      return true;
    }
    return false;
  }

  // ——— Local 3D graph (Obsidian-like monochrome styling) ———

  const GRAPH_CAP = 75;
  const NODE_COLOR = "#b8b8b8";
  const NODE_COLOR_FOCUSED = "#e8e8e8";
  const LINK_COLOR = "rgba(175, 180, 190, 0.72)";

  let forceGraph = null;
  let labelsLayer = null;
  let graphExpanded = false;
  /** @type {Map<string, HTMLElement>} */
  const labelEls = new Map();
  const graphEl = document.getElementById("graph3d");
  const graphNote = document.getElementById("graph-note");
  const graphPanel = document.getElementById("graph-panel");
  const graphBackdrop = document.getElementById("graph-backdrop");
  const graphExpandBtn = document.getElementById("graph-expand-btn");
  const graphPanelHome = graphPanel?.parentElement || null;
  const tocEl = document.getElementById("toc");

  function ensureLabelsLayer() {
    if (!graphEl || labelsLayer) return;
    labelsLayer = document.createElement("div");
    labelsLayer.className = "graph-labels";
    labelsLayer.setAttribute("aria-hidden", "true");
    graphEl.appendChild(labelsLayer);
  }

  function syncGraphLabels() {
    if (!forceGraph || !labelsLayer) return;
    const nodes = forceGraph.graphData()?.nodes || [];
    const seen = new Set();

    for (const n of nodes) {
      const id = n.id;
      if (id == null) continue;
      seen.add(id);
      let el = labelEls.get(id);
      if (!el) {
        el = document.createElement("div");
        el.className = "graph-label";
        labelsLayer.appendChild(el);
        labelEls.set(id, el);
      }
      const text = n.name || n.id || "";
      if (el.textContent !== text) el.textContent = text;
      el.classList.toggle("focused", !!n.focused);
    }

    for (const [id, el] of labelEls) {
      if (!seen.has(id)) {
        el.remove();
        labelEls.delete(id);
      }
    }

    positionGraphLabels();
  }

  function positionGraphLabels() {
    if (!forceGraph || !labelsLayer) return;
    const nodes = forceGraph.graphData()?.nodes || [];
    for (const n of nodes) {
      const el = labelEls.get(n.id);
      if (!el) continue;
      if (n.x == null || n.y == null || n.z == null) {
        el.style.visibility = "hidden";
        continue;
      }
      const coords = forceGraph.graph2ScreenCoords(n.x, n.y, n.z);
      if (!coords || !Number.isFinite(coords.x) || !Number.isFinite(coords.y)) {
        el.style.visibility = "hidden";
        continue;
      }
      el.style.visibility = "visible";
      el.style.transform = `translate(${coords.x}px, ${coords.y}px) translate(-50%, -120%)`;
    }
  }

  function nodeCentroid(nodes) {
    let cx = 0;
    let cy = 0;
    let cz = 0;
    let n = 0;
    for (const node of nodes) {
      if (node.x == null || node.y == null || node.z == null) continue;
      cx += node.x;
      cy += node.y;
      cz += node.z;
      n++;
    }
    if (!n) return { x: 0, y: 0, z: 0 };
    return { x: cx / n, y: cy / n, z: cz / n };
  }

  /** Axis-aligned screen footprint of nodes (px), or null if unusable. */
  function nodeScreenBounds(nodes) {
    if (!forceGraph || !nodes.length) return null;
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    let count = 0;
    for (const node of nodes) {
      if (node.x == null || node.y == null || node.z == null) continue;
      const c = forceGraph.graph2ScreenCoords(node.x, node.y, node.z);
      if (!c || !Number.isFinite(c.x) || !Number.isFinite(c.y)) continue;
      if (c.x < minX) minX = c.x;
      if (c.x > maxX) maxX = c.x;
      if (c.y < minY) minY = c.y;
      if (c.y > maxY) maxY = c.y;
      count++;
    }
    if (!count || minX === Infinity) return null;
    return { minX, minY, maxX, maxY, w: maxX - minX, h: maxY - minY };
  }

  /**
   * Frame the graph to the current canvas. In expanded mode, zoomToFit alone
   * leaves a large empty margin (3D sphere fit is conservative), so we measure
   * the actual screen footprint and pull the camera closer until the cluster
   * fills most of the panel.
   */
  function fitGraphCamera(ms) {
    if (!forceGraph) return;
    const nodes = forceGraph.graphData()?.nodes || [];
    if (!nodes.length) return;

    const duration = ms == null ? (graphExpanded ? 350 : 400) : ms;
    const padPx = graphExpanded ? 28 : 24;

    const finish = () => {
      const after = () => positionGraphLabels();
      if (duration > 0) setTimeout(after, duration + 30);
      else requestAnimationFrame(after);
    };

    if (!graphExpanded) {
      try {
        forceGraph.zoomToFit(duration, padPx);
      } catch (_) {}
      finish();
      return;
    }

    // Baseline frame, then tighten using measured screen footprint.
    try {
      forceGraph.zoomToFit(0, padPx);
    } catch (_) {}

    // Wait a frame so camera matrices match zoomToFit before projecting.
    requestAnimationFrame(() => {
      try {
        // Leave room for node spheres + labels above nodes (not just centers).
        const targetFill = 0.78;
        const bounds = nodeScreenBounds(nodes);
        const cw = graphEl.clientWidth || 1;
        const ch = graphEl.clientHeight || 1;
        if (bounds && bounds.w > 1 && bounds.h > 1) {
          const fill = Math.max(bounds.w / cw, bounds.h / ch);
          if (fill > 0.02 && fill < targetFill) {
            // Perspective: screen size ∝ 1/distance → scale distance by fill/target.
            const scale = fill / targetFill;
            const center = nodeCentroid(nodes);
            const pos = forceGraph.cameraPosition();
            forceGraph.cameraPosition(
              {
                x: center.x + (pos.x - center.x) * scale,
                y: center.y + (pos.y - center.y) * scale,
                z: center.z + (pos.z - center.z) * scale,
              },
              center,
              duration
            );
          }
        }
      } catch (_) {}
      finish();
    });
  }

  function resizeGraph(fit) {
    if (!forceGraph || !graphEl) return;
    const w = graphEl.clientWidth || 260;
    const h = graphEl.clientHeight || 260;
    forceGraph.width(w).height(h);
    positionGraphLabels();
    if (fit) fitGraphCamera();
  }

  function setGraphExpanded(expanded) {
    if (!graphPanel || graphExpanded === expanded) return;
    graphExpanded = expanded;
    document.body.classList.toggle("graph-expanded", expanded);

    if (expanded) {
      // Reparent so the floating panel works even when the right rail is hidden.
      document.body.appendChild(graphPanel);
      if (graphBackdrop) graphBackdrop.hidden = false;
    } else if (graphPanelHome) {
      if (tocEl && tocEl.parentElement === graphPanelHome) {
        graphPanelHome.insertBefore(graphPanel, tocEl);
      } else {
        graphPanelHome.appendChild(graphPanel);
      }
      if (graphBackdrop) graphBackdrop.hidden = true;
    }

    if (graphExpandBtn) {
      graphExpandBtn.setAttribute("aria-expanded", expanded ? "true" : "false");
      graphExpandBtn.setAttribute(
        "aria-label",
        expanded ? "Collapse graph" : "Expand graph"
      );
      graphExpandBtn.title = expanded ? "Collapse graph" : "Expand graph";
    }

    // Wait for CSS layout of the floating panel, then resize canvas + reframe.
    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        resizeGraph(false);
        fitGraphCamera(0);
        // Second pass after flex/WebGL settle — common when panel just reparented.
        setTimeout(() => {
          resizeGraph(false);
          fitGraphCamera(300);
        }, 60);
      });
    });
  }

  function initForceGraph() {
    if (typeof ForceGraph3D !== "function" || !graphEl) return;
    forceGraph = ForceGraph3D()(graphEl)
      .backgroundColor("#1a1a1a")
      .showNavInfo(false)
      .nodeLabel((n) => n.name || n.id)
      .nodeRelSize(3.5)
      .nodeResolution(32) // smooth spheres (default 8 is faceted)
      .nodeOpacity(0.95)
      .nodeVal((n) => n.val || 1)
      .nodeColor((n) => (n.focused ? NODE_COLOR_FOCUSED : NODE_COLOR))
      .linkColor(() => LINK_COLOR)
      .linkOpacity(0.75)
      .linkWidth(0.3)
      .linkDirectionalArrowLength(0)
      .onNodeClick((n) => {
        if (n && n.id && conceptIds.has(n.id)) {
          select({ kind: "concept", id: n.id });
        }
      })
      .onEngineTick(positionGraphLabels);
    // Overlay after the canvas so labels paint on top (Obsidian-style always-on text).
    ensureLabelsLayer();
    // Keep labels aligned while orbiting / zooming after the sim cools.
    try {
      forceGraph.controls()?.addEventListener("change", positionGraphLabels);
    } catch (_) {}
    resizeGraph(false);
  }

  if (graphExpandBtn) {
    graphExpandBtn.addEventListener("click", () => setGraphExpanded(!graphExpanded));
  }
  if (graphBackdrop) {
    graphBackdrop.addEventListener("click", () => setGraphExpanded(false));
  }
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && graphExpanded) {
      e.preventDefault();
      setGraphExpanded(false);
    }
  });

  function collectDirConceptIds(dirId) {
    const prefix = dirId ? dirId + "/" : "";
    const ids = [];
    for (const id of conceptIds) {
      if (!dirId) {
        // root: all concepts (capped later)
        ids.push(id);
      } else if (id === dirId || id.startsWith(prefix)) {
        ids.push(id);
      }
    }
    return ids;
  }

  function buildLocalGraphData() {
    let focusIds = new Set();
    let note = "";

    if (selected.kind === "concept") {
      focusIds.add(selected.id);
      for (const e of graph.edges || []) {
        if (e.source === selected.id) focusIds.add(e.target);
        if (e.target === selected.id) focusIds.add(e.source);
      }
    } else {
      const under = collectDirConceptIds(selected.id);
      if (under.length > GRAPH_CAP) {
        // keep highest weight
        under.sort((a, b) => (nodesById[b]?.weight || 0) - (nodesById[a]?.weight || 0));
        note = `Showing top ${GRAPH_CAP} of ${under.length}`;
        for (const id of under.slice(0, GRAPH_CAP)) focusIds.add(id);
      } else {
        for (const id of under) focusIds.add(id);
      }
    }

    const nodes = [];
    for (const id of focusIds) {
      const n = nodesById[id];
      if (!n) continue;
      const w = n.weight || 0;
      const focused = selected.kind === "concept" && id === selected.id;
      // Modest size variation (Obsidian is nearly uniform; focus a bit larger).
      const val = (focused ? 2.4 : 1.15) + Math.min(w, 1) * 1.6;
      nodes.push({
        id,
        name: n.title || n.label || id,
        val,
        focused,
      });
    }

    const links = [];
    for (const e of graph.edges || []) {
      if (focusIds.has(e.source) && focusIds.has(e.target)) {
        links.push({ source: e.source, target: e.target });
      }
    }

    if (!note && selected.kind === "concept") {
      note = `${nodes.length} nodes · ${links.length} links`;
    } else if (!note) {
      note = `${nodes.length} concepts`;
    }

    return { nodes, links, note };
  }

  function updateLocalGraph() {
    if (!forceGraph) {
      if (typeof ForceGraph3D === "function") initForceGraph();
      else return;
    }
    if (!forceGraph) return;

    const data = buildLocalGraphData();
    if (graphNote) graphNote.textContent = data.note;
    forceGraph.graphData({ nodes: data.nodes, links: data.links });
    syncGraphLabels();

    // re-fit after layout settles a bit
    setTimeout(() => resizeGraph(true), 350);
  }

  window.addEventListener("resize", () => {
    resizeGraph(false);
  });

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
  window.addEventListener("hashchange", () => {
    selectFromHash();
  });

  // Expose for tests
  window.__okfView = {
    select,
    tryNavigateHref,
    resolveHref,
    normalizeToId,
    buildLocalGraphData,
    fitGraphCamera,
    resizeGraph,
    setGraphExpanded,
    get graphExpanded() {
      return graphExpanded;
    },
    get forceGraph() {
      return forceGraph;
    },
    get selected() {
      return selected;
    },
    basePathForSelection,
  };

  initForceGraph();
  renderTree("");
  if (!selectFromHash()) {
    select({ kind: "dir", id: "" });
  }
})();
