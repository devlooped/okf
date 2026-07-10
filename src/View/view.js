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

  // ——— Tag index (count + co-occurrence) ———

  /**
   * @typedef {{ count: number, members: string[] }} TagEntry
   * @type {{ byTag: Map<string, TagEntry>, co: Map<string, number>, graphData: { nodes: object[], links: object[] } }}
   */
  const tagIndex = buildTagIndex();

  function buildTagIndex() {
    /** @type {Map<string, TagEntry>} */
    const byTag = new Map();
    /** @type {Map<string, number>} */
    const co = new Map();

    function pairKey(a, b) {
      return a < b ? a + "\0" + b : b + "\0" + a;
    }

    for (const n of graph.nodes || []) {
      const tags = (n.tags || []).filter((t) => t != null && String(t).trim() !== "");
      if (!tags.length) continue;
      // de-dupe within a document
      const unique = [...new Set(tags.map((t) => String(t)))];
      for (const t of unique) {
        let entry = byTag.get(t);
        if (!entry) {
          entry = { count: 0, members: [] };
          byTag.set(t, entry);
        }
        entry.count++;
        entry.members.push(n.id);
      }
      for (let i = 0; i < unique.length; i++) {
        for (let j = i + 1; j < unique.length; j++) {
          const k = pairKey(unique[i], unique[j]);
          co.set(k, (co.get(k) || 0) + 1);
        }
      }
    }

    const nodes = [];
    for (const [tag, entry] of byTag) {
      // Log-scale size so hub tags (e.g. bastiat) do not dominate completely.
      const val = 1.2 + Math.log2(entry.count + 1) * 1.8;
      nodes.push({
        id: tag,
        name: tag,
        val,
        count: entry.count,
        focused: false,
      });
    }

    const links = [];
    for (const [k, coCount] of co) {
      const sep = k.indexOf("\0");
      const a = k.slice(0, sep);
      const b = k.slice(sep + 1);
      const ca = byTag.get(a)?.count || 1;
      const cb = byTag.get(b)?.count || 1;
      const jaccard = coCount / (ca + cb - coCount);
      links.push({
        source: a,
        target: b,
        co: coCount,
        jaccard,
        // Width / visual weight from co-count; force strength uses Jaccard.
        value: coCount,
      });
    }

    return {
      byTag,
      co,
      graphData: { nodes, links },
    };
  }

  function tagExists(tag) {
    return tagIndex.byTag.has(tag);
  }

  /** @type {{ kind: 'dir'|'concept'|'tag', id: string }} */
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
    if (tagsExpanded) updateTagCloudHighlight();
  }

  // ——— Hash routing (#c/id, #d/id, #t/tag) ———

  function updateHash() {
    try {
      let prefix;
      if (selected.kind === "dir") prefix = "d/";
      else if (selected.kind === "tag") prefix = "t/";
      else prefix = "c/";
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
    } else if (raw.startsWith("t/")) {
      const id = raw.slice(2);
      if (tagExists(id)) {
        select({ kind: "tag", id }, { skipHash: true });
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
  const NODE_COLOR_HIGHLIGHT = "#e4e0ff";
  const NODE_COLOR_DIM = "#3a3a40";
  const LINK_COLOR = "rgba(175, 180, 190, 0.55)";
  const LINK_COLOR_HIGHLIGHT = "rgba(124, 108, 245, 0.92)";
  const LINK_COLOR_DIM = "rgba(90, 90, 100, 0.12)";
  /** Delay before hover highlight applies (Obsidian-style, avoids flicker). */
  const GRAPH_HOVER_DELAY_MS = 220;
  /** nodeRelSize used for world-radius → screen offset estimates. */
  const GRAPH_NODE_REL_SIZE = 3.5;
  /** linkWidth 0 → THREE.Line (hairline); >0 draws thick cylinders. */
  const GRAPH_LINK_WIDTH = 0;

  let forceGraph = null;
  let labelsLayer = null;
  let graphExpanded = false;
  /** @type {string | null} currently applied highlight root (after delay) */
  let graphHoverId = null;
  /** @type {string | null} node under cursor (may differ until delay fires) */
  let graphHoverPendingId = null;
  /** @type {ReturnType<typeof setTimeout> | null} */
  let graphHoverTimer = null;
  /** @type {Set<string>} */
  let graphHoverNodeIds = new Set();
  /** @type {Set<string>} */
  let graphHoverLinkKeys = new Set();
  /** @type {Map<string, HTMLElement>} */
  const labelEls = new Map();
  const graphEl = document.getElementById("graph3d");
  const graphNote = document.getElementById("graph-note");
  const graphPanel = document.getElementById("graph-panel");
  const graphBackdrop = document.getElementById("graph-backdrop");
  const graphExpandBtn = document.getElementById("graph-expand-btn");
  const graphPanelHome = graphPanel?.parentElement || null;
  const tocEl = document.getElementById("toc");

  function linkEndpoints(link) {
    const s = typeof link.source === "object" ? link.source.id : link.source;
    const t = typeof link.target === "object" ? link.target.id : link.target;
    return [s, t];
  }

  function undirectedLinkKey(a, b) {
    return a < b ? a + "\0" + b : b + "\0" + a;
  }

  /**
   * Estimate node sphere radius in screen px so labels clear the mesh.
   * Projects center and a world-space offset along the camera's local up.
   */
  function nodeScreenRadiusPx(node) {
    if (!forceGraph || node.x == null || node.y == null || node.z == null) {
      return 12 + Math.cbrt(node?.val || 1) * 8;
    }
    const worldR = GRAPH_NODE_REL_SIZE * Math.cbrt(node.val || 1);
    const c = forceGraph.graph2ScreenCoords(node.x, node.y, node.z);
    // Offset along world +Y (camera often looks from +Z; +Y is a stable axis).
    const e = forceGraph.graph2ScreenCoords(node.x, node.y + worldR, node.z);
    if (
      c &&
      e &&
      Number.isFinite(c.x) &&
      Number.isFinite(c.y) &&
      Number.isFinite(e.x) &&
      Number.isFinite(e.y)
    ) {
      const d = Math.hypot(e.x - c.x, e.y - c.y);
      if (d > 2 && d < 200) return d;
    }
    return 12 + Math.cbrt(node.val || 1) * 8;
  }

  /** Screen-space Y offset: label sits just under the sphere (Obsidian-style). */
  function labelOffsetBelow(node) {
    return nodeScreenRadiusPx(node) + 4;
  }

  function ensureLabelsLayer() {
    if (!graphEl || labelsLayer) return;
    labelsLayer = document.createElement("div");
    labelsLayer.className = "graph-labels";
    labelsLayer.setAttribute("aria-hidden", "true");
    graphEl.appendChild(labelsLayer);
  }

  /** Outer shell is positioned by JS; inner .graph-label-text animates size/nudge via CSS. */
  function createGraphLabelEl() {
    const el = document.createElement("div");
    el.className = "graph-label";
    const textEl = document.createElement("span");
    textEl.className = "graph-label-text";
    el.appendChild(textEl);
    return el;
  }

  function setGraphLabelText(el, text) {
    const textEl = el.querySelector(".graph-label-text");
    if (textEl) {
      if (textEl.textContent !== text) textEl.textContent = text;
    } else if (el.textContent !== text) {
      el.textContent = text;
    }
  }

  function applyLabelHighlightState(el, id, focused) {
    const hovering = graphHoverId != null;
    const hi = hovering && graphHoverNodeIds.has(id);
    const root = hovering && graphHoverId === id;
    el.classList.toggle("focused", !!focused && !hovering);
    el.classList.toggle("highlighted", hi);
    el.classList.toggle("hover-root", root);
    el.classList.toggle("dimmed", hovering && !hi);
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
        el = createGraphLabelEl();
        labelsLayer.appendChild(el);
        labelEls.set(id, el);
      }
      setGraphLabelText(el, n.name || n.id || "");
      applyLabelHighlightState(el, id, n.focused);
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
      // Position only — hover size/nudge is CSS on .graph-label-text (gradual transition).
      const y = coords.y + labelOffsetBelow(n);
      el.style.transform = `translate(${coords.x}px, ${y}px) translate(-50%, 0)`;
    }
  }

  function graphNodeColor(n) {
    if (graphHoverId != null) {
      return graphHoverNodeIds.has(n.id) ? NODE_COLOR_HIGHLIGHT : NODE_COLOR_DIM;
    }
    return n.focused ? NODE_COLOR_FOCUSED : NODE_COLOR;
  }

  function graphLinkColor(l) {
    if (graphHoverId != null) {
      const [s, t] = linkEndpoints(l);
      if (s != null && t != null && graphHoverLinkKeys.has(undirectedLinkKey(s, t))) {
        return LINK_COLOR_HIGHLIGHT;
      }
      return LINK_COLOR_DIM;
    }
    return LINK_COLOR;
  }

  function refreshGraphHighlightMaterials() {
    if (!forceGraph) return;
    // Re-apply accessors so three-forcegraph rebuilds node/link materials.
    // Do not touch linkWidth — width stays constant (hairline); only colors change.
    forceGraph.nodeColor(forceGraph.nodeColor()).linkColor(forceGraph.linkColor());
  }

  function applyGraphHover(node) {
    const id = node && node.id != null ? node.id : null;
    if (id === graphHoverId) return;

    graphHoverId = id;
    graphHoverNodeIds = new Set();
    graphHoverLinkKeys = new Set();

    if (id != null && forceGraph) {
      graphHoverNodeIds.add(id);
      for (const l of forceGraph.graphData()?.links || []) {
        const [s, t] = linkEndpoints(l);
        if (s === id || t === id) {
          if (s != null) graphHoverNodeIds.add(s);
          if (t != null) graphHoverNodeIds.add(t);
          if (s != null && t != null) graphHoverLinkKeys.add(undirectedLinkKey(s, t));
        }
      }
    }

    refreshGraphHighlightMaterials();
    // Class toggles drive CSS transitions on .graph-label-text (move + size).
    for (const n of forceGraph?.graphData()?.nodes || []) {
      const el = labelEls.get(n.id);
      if (el) applyLabelHighlightState(el, n.id, n.focused);
    }
  }

  function clearGraphHoverTimer() {
    if (graphHoverTimer != null) {
      clearTimeout(graphHoverTimer);
      graphHoverTimer = null;
    }
  }

  /**
   * Schedule hover highlight. Entering a node waits briefly (anti-flicker);
   * leaving clears promptly. Moving between nodes resets the delay.
   */
  function onGraphNodeHover(node) {
    const id = node && node.id != null ? node.id : null;
    if (graphEl) graphEl.style.cursor = id != null ? "pointer" : null;

    if (id === graphHoverPendingId) return;
    graphHoverPendingId = id;
    clearGraphHoverTimer();

    if (id == null) {
      // Leave: clear soon so highlight doesn't stick while still scanning.
      graphHoverTimer = setTimeout(() => {
        graphHoverTimer = null;
        applyGraphHover(null);
      }, 60);
      return;
    }

    // Already highlighting this node (e.g. re-enter after brief leave cancel).
    if (id === graphHoverId) return;

    graphHoverTimer = setTimeout(() => {
      graphHoverTimer = null;
      if (graphHoverPendingId !== id) return;
      // Re-resolve in case graphData was swapped during the delay.
      const live =
        (forceGraph?.graphData()?.nodes || []).find((n) => n.id === id) || { id };
      applyGraphHover(live);
    }, GRAPH_HOVER_DELAY_MS);
  }

  function clearGraphHover() {
    clearGraphHoverTimer();
    graphHoverPendingId = null;
    if (graphHoverId == null && graphHoverNodeIds.size === 0) return;
    applyGraphHover(null);
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
  function nodeScreenBounds(nodes, graphInstance, el) {
    if (!graphInstance || !nodes.length) return null;
    let minX = Infinity;
    let minY = Infinity;
    let maxX = -Infinity;
    let maxY = -Infinity;
    let count = 0;
    for (const node of nodes) {
      if (node.x == null || node.y == null || node.z == null) continue;
      const c = graphInstance.graph2ScreenCoords(node.x, node.y, node.z);
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
        // Leave room for node spheres + labels below nodes (not just centers).
        const targetFill = 0.78;
        const bounds = nodeScreenBounds(nodes, forceGraph, graphEl);
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
    const w = Math.max(1, graphEl.clientWidth || 260);
    const h = Math.max(1, graphEl.clientHeight || 260);
    forceGraph.width(w).height(h);
    // Keep projection aspect in sync — mismatched aspect stretches spheres into eggs.
    try {
      const cam = forceGraph.camera();
      if (cam) {
        cam.aspect = w / h;
        cam.updateProjectionMatrix();
      }
    } catch (_) {}
    positionGraphLabels();
    if (fit) fitGraphCamera();
  }

  function setGraphExpanded(expanded) {
    if (!graphPanel || graphExpanded === expanded) return;
    if (expanded) setTagsExpanded(false);
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
      // Always-on HTML labels replace the default hover tooltip (avoids text on the sphere).
      .nodeLabel(() => null)
      .nodeRelSize(GRAPH_NODE_REL_SIZE)
      .nodeResolution(48) // smoother spheres (default 8 is faceted / egg-looking)
      .nodeOpacity(0.95)
      .nodeVal((n) => n.val || 1)
      .nodeColor(graphNodeColor)
      .linkColor(graphLinkColor)
      .linkOpacity(0.85)
      // 0 → THREE.Line (hairline). Non-zero draws thick cylinders.
      .linkWidth(GRAPH_LINK_WIDTH)
      .linkDirectionalArrowLength(0)
      .onNodeClick((n) => {
        if (n && n.id && conceptIds.has(n.id)) {
          select({ kind: "concept", id: n.id });
        }
      })
      .onNodeHover(onGraphNodeHover)
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

    if (selected.kind === "tag") {
      return { nodes: [], links: [], note: "Select a concept" };
    }

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

    clearGraphHover();
    const data = buildLocalGraphData();
    if (graphNote) graphNote.textContent = data.note;
    forceGraph.graphData({ nodes: data.nodes, links: data.links });
    syncGraphLabels();

    // re-fit after layout settles a bit
    setTimeout(() => resizeGraph(true), 350);
  }

  // ——— Tag cloud floating panel ———

  let tagForceGraph = null;
  let tagLabelsLayer = null;
  let tagsExpanded = false;
  /** @type {Map<string, HTMLElement>} */
  const tagLabelEls = new Map();
  const tagPanel = document.getElementById("tag-panel");
  const tagCloudEl = document.getElementById("tag-cloud");
  const tagNote = document.getElementById("tag-note");
  const tagBackdrop = document.getElementById("tag-backdrop");
  const tagOpenBtn = document.getElementById("tags-open-btn");
  const tagCloseBtn = document.getElementById("tag-close-btn");
  const tagEmptyEl = document.getElementById("tag-empty");

  function ensureTagLabelsLayer() {
    if (!tagCloudEl || tagLabelsLayer) return;
    tagLabelsLayer = document.createElement("div");
    tagLabelsLayer.className = "graph-labels";
    tagLabelsLayer.setAttribute("aria-hidden", "true");
    tagCloudEl.appendChild(tagLabelsLayer);
  }

  function syncTagLabels() {
    if (!tagForceGraph || !tagLabelsLayer) return;
    const nodes = tagForceGraph.graphData()?.nodes || [];
    const seen = new Set();

    for (const n of nodes) {
      const id = n.id;
      if (id == null) continue;
      seen.add(id);
      let el = tagLabelEls.get(id);
      if (!el) {
        el = createGraphLabelEl();
        tagLabelsLayer.appendChild(el);
        tagLabelEls.set(id, el);
      }
      setGraphLabelText(el, n.name || n.id || "");
      el.classList.toggle("focused", !!n.focused);
    }

    for (const [id, el] of tagLabelEls) {
      if (!seen.has(id)) {
        el.remove();
        tagLabelEls.delete(id);
      }
    }

    positionTagLabels();
  }

  function positionTagLabels() {
    if (!tagForceGraph || !tagLabelsLayer) return;
    const nodes = tagForceGraph.graphData()?.nodes || [];
    for (const n of nodes) {
      const el = tagLabelEls.get(n.id);
      if (!el) continue;
      if (n.x == null || n.y == null || n.z == null) {
        el.style.visibility = "hidden";
        continue;
      }
      const coords = tagForceGraph.graph2ScreenCoords(n.x, n.y, n.z);
      if (!coords || !Number.isFinite(coords.x) || !Number.isFinite(coords.y)) {
        el.style.visibility = "hidden";
        continue;
      }
      el.style.visibility = "visible";
      const y = coords.y + labelOffsetBelow(n);
      el.style.transform = `translate(${coords.x}px, ${y}px) translate(-50%, 0)`;
    }
  }

  function fitTagCamera(ms) {
    if (!tagForceGraph) return;
    const nodes = tagForceGraph.graphData()?.nodes || [];
    if (!nodes.length) return;

    const duration = ms == null ? 350 : ms;
    const padPx = 28;

    try {
      tagForceGraph.zoomToFit(0, padPx);
    } catch (_) {}

    requestAnimationFrame(() => {
      try {
        const targetFill = 0.78;
        const bounds = nodeScreenBounds(nodes, tagForceGraph, tagCloudEl);
        const cw = tagCloudEl.clientWidth || 1;
        const ch = tagCloudEl.clientHeight || 1;
        if (bounds && bounds.w > 1 && bounds.h > 1) {
          const fill = Math.max(bounds.w / cw, bounds.h / ch);
          if (fill > 0.02 && fill < targetFill) {
            const scale = fill / targetFill;
            const center = nodeCentroid(nodes);
            const pos = tagForceGraph.cameraPosition();
            tagForceGraph.cameraPosition(
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
      const after = () => positionTagLabels();
      if (duration > 0) setTimeout(after, duration + 30);
      else requestAnimationFrame(after);
    });
  }

  function resizeTagGraph(fit) {
    if (!tagForceGraph || !tagCloudEl) return;
    const w = Math.max(1, tagCloudEl.clientWidth || 400);
    const h = Math.max(1, tagCloudEl.clientHeight || 400);
    tagForceGraph.width(w).height(h);
    try {
      const cam = tagForceGraph.camera();
      if (cam) {
        cam.aspect = w / h;
        cam.updateProjectionMatrix();
      }
    } catch (_) {}
    positionTagLabels();
    if (fit) fitTagCamera();
  }

  function buildTagGraphPayload() {
    const focusedTag = selected.kind === "tag" ? selected.id : null;
    const nodes = tagIndex.graphData.nodes.map((n) => ({
      ...n,
      focused: focusedTag != null && n.id === focusedTag,
    }));
    // Fresh link objects so force-graph can rebind source/target refs.
    const links = tagIndex.graphData.links.map((l) => ({
      source: typeof l.source === "object" ? l.source.id : l.source,
      target: typeof l.target === "object" ? l.target.id : l.target,
      co: l.co,
      jaccard: l.jaccard,
      value: l.value,
    }));
    return { nodes, links };
  }

  function updateTagCloudHighlight() {
    if (!tagForceGraph) return;
    const data = buildTagGraphPayload();
    // Preserve simulation positions by matching existing nodes when possible.
    const prev = tagForceGraph.graphData();
    const prevById = new Map((prev.nodes || []).map((n) => [n.id, n]));
    for (const n of data.nodes) {
      const old = prevById.get(n.id);
      if (old) {
        if (old.x != null) n.x = old.x;
        if (old.y != null) n.y = old.y;
        if (old.z != null) n.z = old.z;
        if (old.vx != null) n.vx = old.vx;
        if (old.vy != null) n.vy = old.vy;
        if (old.vz != null) n.vz = old.vz;
      }
    }
    tagForceGraph.graphData({ nodes: data.nodes, links: data.links });
    syncTagLabels();
  }

  function initTagForceGraph() {
    if (typeof ForceGraph3D !== "function" || !tagCloudEl) return;
    if (tagForceGraph) return;

    tagForceGraph = ForceGraph3D()(tagCloudEl)
      .backgroundColor("#1a1a1a")
      .showNavInfo(false)
      .nodeLabel(() => null)
      .nodeRelSize(4)
      .nodeResolution(48)
      .nodeOpacity(0.95)
      .nodeVal((n) => n.val || 1)
      .nodeColor((n) => (n.focused ? NODE_COLOR_FOCUSED : NODE_COLOR))
      .linkColor((l) => {
        const j = l.jaccard != null ? l.jaccard : 0.2;
        const a = 0.25 + Math.min(j, 1) * 0.55;
        return `rgba(175, 180, 190, ${a})`;
      })
      .linkOpacity(0.85)
      // Hairline lines (width 0); co-occurrence strength is color-only, not thickness.
      .linkWidth(0)
      .linkDirectionalArrowLength(0)
      .onNodeClick((n) => {
        if (n && n.id && tagExists(n.id)) {
          select({ kind: "tag", id: n.id });
          // Keep panel open so proximity remains visible while browsing tags.
        }
      })
      .onEngineTick(positionTagLabels);

    // Link distance / strength from Jaccard (hub tags pull less universally).
    try {
      const linkForce = tagForceGraph.d3Force("link");
      if (linkForce) {
        linkForce
          .distance((l) => {
            const j = l.jaccard != null ? l.jaccard : 0.1;
            return 28 + (1 - Math.min(j, 1)) * 90;
          })
          .strength((l) => {
            const j = l.jaccard != null ? l.jaccard : 0.05;
            return 0.05 + Math.min(j, 1) * 0.55;
          });
      }
      const charge = tagForceGraph.d3Force("charge");
      if (charge && typeof charge.strength === "function") {
        charge.strength(-45);
      }
    } catch (_) {}

    ensureTagLabelsLayer();
    try {
      tagForceGraph.controls()?.addEventListener("change", positionTagLabels);
    } catch (_) {}

    const data = buildTagGraphPayload();
    tagForceGraph.graphData({ nodes: data.nodes, links: data.links });
    syncTagLabels();
  }

  function setTagsExpanded(expanded) {
    if (!tagPanel || tagsExpanded === expanded) return;
    if (expanded) setGraphExpanded(false);
    tagsExpanded = expanded;
    document.body.classList.toggle("tags-expanded", expanded);
    tagPanel.hidden = !expanded;
    if (tagBackdrop) tagBackdrop.hidden = !expanded;

    if (tagOpenBtn) {
      tagOpenBtn.setAttribute("aria-expanded", expanded ? "true" : "false");
    }

    if (!expanded) return;

    const hasTags = tagIndex.byTag.size > 0;
    if (tagEmptyEl) tagEmptyEl.hidden = hasTags;
    if (tagCloudEl) tagCloudEl.hidden = !hasTags;
    if (tagNote) {
      if (!hasTags) {
        tagNote.textContent = "0 tags";
      } else {
        const n = tagIndex.graphData.nodes.length;
        const m = tagIndex.graphData.links.length;
        tagNote.textContent = `${n} tags · ${m} links`;
      }
    }

    if (!hasTags) return;

    if (!tagForceGraph && typeof ForceGraph3D === "function") {
      initTagForceGraph();
    } else if (tagForceGraph) {
      updateTagCloudHighlight();
    }

    requestAnimationFrame(() => {
      requestAnimationFrame(() => {
        resizeTagGraph(false);
        fitTagCamera(0);
        setTimeout(() => {
          resizeTagGraph(false);
          fitTagCamera(300);
        }, 60);
      });
    });
  }

  if (tagOpenBtn) {
    tagOpenBtn.addEventListener("click", () => setTagsExpanded(!tagsExpanded));
  }
  if (tagCloseBtn) {
    tagCloseBtn.addEventListener("click", () => setTagsExpanded(false));
  }
  if (tagBackdrop) {
    tagBackdrop.addEventListener("click", () => setTagsExpanded(false));
  }

  document.addEventListener("keydown", (e) => {
    if (e.key !== "Escape") return;
    if (tagsExpanded) {
      e.preventDefault();
      setTagsExpanded(false);
      return;
    }
    if (graphExpanded) {
      e.preventDefault();
      setGraphExpanded(false);
    }
  });

  window.addEventListener("resize", () => {
    resizeGraph(false);
    if (tagsExpanded) resizeTagGraph(false);
  });

  function renderTagContent(tag) {
    const titleEl = document.getElementById("content-title");
    const descEl = document.getElementById("content-desc");
    const typeEl = document.getElementById("content-type");
    const metaEl = document.getElementById("content-meta");
    const bodyEl = document.getElementById("content-body");
    const blSection = document.getElementById("backlinks");
    const blList = document.getElementById("backlinks-list");

    metaEl.innerHTML = "";
    blList.innerHTML = "";
    blSection.hidden = true;

    const entry = tagIndex.byTag.get(tag);
    titleEl.textContent = tag;
    typeEl.hidden = false;
    typeEl.textContent = "Tag";

    const count = entry ? entry.count : 0;
    descEl.textContent =
      count === 1 ? "Used on 1 concept" : `Used on ${count} concepts`;

    bodyEl.innerHTML = "";
    if (!entry || !entry.members.length) {
      const p = document.createElement("p");
      p.className = "tag-results-empty";
      p.textContent = "No concepts use this tag.";
      bodyEl.appendChild(p);
      buildToc(bodyEl);
      return;
    }

    const members = entry.members.slice().sort((a, b) => {
      const ta = (nodesById[a]?.title || nodesById[a]?.label || a).toLowerCase();
      const tb = (nodesById[b]?.title || nodesById[b]?.label || b).toLowerCase();
      return ta.localeCompare(tb);
    });

    const ul = document.createElement("ul");
    ul.className = "tag-results";
    for (const id of members) {
      const n = nodesById[id];
      const li = document.createElement("li");

      const titleBtn = document.createElement("button");
      titleBtn.type = "button";
      titleBtn.className = "tag-result-title";
      titleBtn.textContent = n?.title || n?.label || id;
      titleBtn.addEventListener("click", () => select({ kind: "concept", id }));
      li.appendChild(titleBtn);

      if (n?.type) {
        const chip = document.createElement("span");
        chip.className = "tag-result-type";
        chip.textContent = n.type;
        li.appendChild(chip);
      }

      if (n?.description) {
        const d = document.createElement("p");
        d.className = "tag-result-desc";
        d.textContent = n.description;
        li.appendChild(d);
      }

      ul.appendChild(li);
    }
    bodyEl.appendChild(ul);
    buildToc(bodyEl);
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

    if (selected.kind === "tag") {
      renderTagContent(selected.id);
      return;
    }

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
          const btn = document.createElement("button");
          btn.type = "button";
          btn.className = "tag";
          btn.textContent = t;
          btn.title = `Show concepts tagged “${t}”`;
          btn.addEventListener("click", () => select({ kind: "tag", id: t }));
          metaEl.appendChild(btn);
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
    buildTagIndex,
    fitGraphCamera,
    resizeGraph,
    setGraphExpanded,
    setTagsExpanded,
    get graphExpanded() {
      return graphExpanded;
    },
    get tagsExpanded() {
      return tagsExpanded;
    },
    get forceGraph() {
      return forceGraph;
    },
    get tagForceGraph() {
      return tagForceGraph;
    },
    get tagIndex() {
      return tagIndex;
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
