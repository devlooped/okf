(function () {
  const bundle = window.BUNDLE;
  const bundleName = window.BUNDLE_NAME;
  document.title = `${bundleName} — OKF Viewer`;
  document.getElementById("bundle-name").textContent = bundleName;

  // Populate type filter
  const typeSelect = document.getElementById("filter-type");
  for (const t of bundle.types) {
    const opt = document.createElement("option");
    opt.value = t;
    opt.textContent = t;
    typeSelect.appendChild(opt);
  }

  // Build reverse-link index for backlinks
  const backlinks = {};
  for (const edge of bundle.edges) {
    const { source, target } = edge.data;
    (backlinks[target] ||= []).push(source);
  }

  // Look up node label/type by id
  const nodeIndex = {};
  const slugToId = {};
  for (const n of bundle.nodes) {
    const d = n.data;
    nodeIndex[d.id] = d;
    if (d.slug) slugToId[d.slug] = d.id;
  }

  // Create Cytoscape instance WITHOUT an initial layout so we can
  // pre-position high-weight nodes near the center.
  const cy = cytoscape({
    container: document.getElementById("graph"),
    elements: [...bundle.nodes, ...bundle.edges],
    style: [
      {
        selector: "node",
        style: {
          "background-color": "data(color)",
          "label": "data(label)",
          "color": "#0f172a",
          "font-size": "data(fontSize)",
          "font-weight": 500,
          "text-valign": "bottom",
          "text-halign": "center",
          "text-margin-y": 6,
          "text-wrap": "wrap",
          "text-max-width": 140,
          // Label readability: white rounded background so text doesn't disappear
          // on top of edges or other nodes
          "text-background-color": "#ffffff",
          "text-background-opacity": 0.88,
          "text-background-shape": "roundrectangle",
          "text-background-padding": 3,
          "width": "data(size)",
          "height": "data(size)",
          "border-width": 1,
          "border-color": "#0f172a",
          "z-index": 10,
        },
      },
      {
        selector: "node[?stale]",
        style: {
          "border-width": 2,
          "border-color": "#b91c1c",
          "border-style": "dashed",
        },
      },
      {
        selector: 'node[status = "deprecated"]',
        style: {
          "opacity": 0.55,
        },
      },
      {
        selector: "node:selected",
        style: {
          "border-width": 3,
          "border-color": "#f59e0b",
        },
      },
      {
        selector: "edge",
        style: {
          "width": 1.5,
          "line-color": "#cbd5e1",
          "target-arrow-color": "#cbd5e1",
          "target-arrow-shape": "triangle",
          "curve-style": "bezier",
          "arrow-scale": 0.9,
        },
      },
      {
        selector: "edge:selected",
        style: {
          "line-color": "#f59e0b",
          "target-arrow-color": "#f59e0b",
          "width": 2.5,
        },
      },
      {
        selector: ".dim",
        style: { "opacity": 0.15 },
      },
    ],
    wheelSensitivity: 0.2,
  });

  // Pre-position the highest-weight nodes near the center.
  // This strongly encourages the force layout to keep important hubs central
  // and produces much nicer results for bundles that have a few dominant concepts.
  try {
    const withWeight = bundle.nodes
      .map((n) => ({ id: n.data.id, w: n.data.weight || 0 }))
      .sort((a, b) => b.w - a.w);
    const top = withWeight.slice(0, 7);
    const cx = 0;
    const cyC = 0;
    top.forEach((item, i) => {
      const el = cy.getElementById(item.id);
      if (el && el.length > 0) {
        const angle = (2 * Math.PI * i) / Math.max(1, top.length);
        const radius = 55 + (i % 3) * 22;
        el.position({
          x: cx + Math.cos(angle) * radius,
          y: cyC + Math.sin(angle) * radius,
        });
      }
    });
  } catch (e) {
    // non-fatal
  }

  // Improved COSE defaults: more spread, respect labels for positioning,
  // stronger central gravity (helps high-weight nodes stay central),
  // and nodeDimensionsIncludeLabels so the layout accounts for text size.
  const runCose = () => {
    cy.layout({
      name: "cose",
      animate: false,
      padding: 55,
      nodeRepulsion: 7200,
      idealEdgeLength: 52,
      edgeElasticity: 95,
      nestingFactor: 1.25,
      gravity: 1.9, // stronger pull toward center benefits high-weight hubs
      numIter: 1400,
      initialTemp: 180,
      coolingFactor: 0.955,
      minTemp: 0.9,
      nodeDimensionsIncludeLabels: true,
    }).run();
  };

  // Run improved cose on init
  runCose();

  cy.on("tap", "node", (evt) => showDetail(evt.target.id()));
  cy.on("tap", (evt) => {
    if (evt.target === cy) clearSelection();
  });

  document.getElementById("layout").addEventListener("change", (e) => {
    const name = e.target.value;
    if (name === "cose") {
      runCose();
    } else {
      // Reasonable settings for other built-in layouts
      const opts = { name, animate: false, padding: 50 };
      if (name === "concentric" || name === "circle") {
        opts.nodeDimensionsIncludeLabels = true;
      }
      cy.layout(opts).run();
    }
  });

  document.getElementById("reset").addEventListener("click", () => {
    cy.fit(null, 30);
    clearSelection();
  });

  document.getElementById("search").addEventListener("input", (e) => {
    const q = e.target.value.trim().toLowerCase();
    if (!q) {
      cy.elements().removeClass("dim");
      return;
    }
    cy.nodes().forEach((n) => {
      const d = n.data();
      const hay =
        (d.label || "").toLowerCase() + " " +
        d.id.toLowerCase() + " " +
        (d.tags || []).join(" ").toLowerCase();
      n.toggleClass("dim", !hay.includes(q));
    });
    cy.edges().forEach((edge) => {
      const src = edge.source();
      const tgt = edge.target();
      edge.toggleClass("dim", src.hasClass("dim") || tgt.hasClass("dim"));
    });
  });

  document.getElementById("filter-type").addEventListener("change", (e) => {
    const t = e.target.value;
    if (!t) {
      cy.elements().removeClass("dim");
      return;
    }
    cy.nodes().forEach((n) => {
      n.toggleClass("dim", n.data("type") !== t);
    });
    cy.edges().forEach((edge) => {
      edge.toggleClass("dim", edge.source().hasClass("dim") || edge.target().hasClass("dim"));
    });
  });

  function clearSelection() {
    cy.elements().unselect();
    // Clear hash for clean state (replace to avoid history spam)
    if (location.hash) {
      try { history.replaceState(null, "", location.pathname + location.search); } catch (_) {}
    }
    document.getElementById("detail-empty").hidden = false;
    document.getElementById("detail-content").hidden = true;
  }

  function getIdFromHash() {
    const h = location.hash;
    if (!h || h === "#") return null;
    let candidate;
    try {
      candidate = decodeURIComponent(h.slice(1));
    } catch (_) {
      candidate = h.slice(1);
    }
    if (nodeIndex[candidate]) return candidate;
    if (slugToId[candidate]) return slugToId[candidate];
    return null;
  }

  function showDetail(conceptId) {
    const data = nodeIndex[conceptId];
    if (!data) return;

    // Update URL hash using the slug (nice + short) when available; falls back to full id.
    // This gives pretty shareable links like #fl instead of #fundamental-principles/...
    const hashId = data.slug || conceptId;

    try {
      // encode but keep slashes pretty for readability
      const newHash = "#" + encodeURIComponent(hashId).replace(/%2F/gi, "/");
      if (location.hash !== newHash) {
        history.replaceState(null, "", newHash);
      }
    } catch (_) {}

    cy.elements().unselect();
    const node = cy.getElementById(conceptId);
    if (node) node.select();

    document.getElementById("detail-empty").hidden = true;
    const content = document.getElementById("detail-content");
    content.hidden = false;

    const chip = document.getElementById("detail-type");
    chip.textContent = data.type;
    chip.style.background = data.color;

    document.getElementById("detail-title").textContent = data.label;
    document.getElementById("detail-id").textContent = conceptId;
    document.getElementById("detail-description").textContent = data.description || "—";

    const resourceEl = document.getElementById("detail-resource");
    resourceEl.innerHTML = "";
    if (data.resource) {
      const a = document.createElement("a");
      a.href = data.resource;
      a.textContent = data.resource;
      a.target = "_blank";
      a.rel = "noopener";
      a.className = "external";
      resourceEl.appendChild(a);
    } else {
      resourceEl.textContent = "—";
    }

    const tagsEl = document.getElementById("detail-tags");
    tagsEl.innerHTML = "";
    if (data.tags && data.tags.length) {
      for (const t of data.tags) {
        const span = document.createElement("span");
        span.className = "tag";
        span.textContent = t;
        tagsEl.appendChild(span);
      }
    } else {
      tagsEl.textContent = "—";
    }

    const badgesEl = document.getElementById("detail-badges");
    badgesEl.innerHTML = "";
    const status = data.status || "stable";
    badgesEl.appendChild(makeBadge(status, "status-" + status));
    const tier = data.trustTier || "unverified";
    badgesEl.appendChild(makeBadge(tier, "trust-" + tier));
    if (data.stale) {
      const label = data.staleAfter ? `stale (since ${data.staleAfter})` : "stale";
      badgesEl.appendChild(makeBadge(label, "stale"));
    } else if (data.staleAfter) {
      badgesEl.appendChild(makeBadge(`stale after ${data.staleAfter}`, "fresh"));
    }

    document.getElementById("detail-generated").textContent = formatActorEvent(data.generated);

    const verifiedEl = document.getElementById("detail-verified");
    const verified = data.verified || [];
    verifiedEl.textContent = verified.length
      ? verified.map(formatActorEvent).join("; ")
      : "—";

    const sourcesEl = document.getElementById("detail-sources");
    sourcesEl.innerHTML = "";
    const sources = data.sources || [];
    if (sources.length) {
      const ul = document.createElement("ul");
      ul.className = "sources-list";
      for (const s of sources) {
        const li = document.createElement("li");
        const label = s.title || s.resource || s.id || "source";
        if (s.resource && /^https?:\/\//.test(s.resource)) {
          const a = document.createElement("a");
          a.href = s.resource;
          a.textContent = label;
          a.target = "_blank";
          a.rel = "noopener";
          a.className = "external";
          li.appendChild(a);
        } else {
          li.textContent = s.resource ? `${label} (${s.resource})` : label;
        }
        ul.appendChild(li);
      }
      sourcesEl.appendChild(ul);
    } else {
      sourcesEl.textContent = "—";
    }

    const body = bundle.bodies[conceptId] || "";
    const html = marked.parse(body, { breaks: false, gfm: true });
    const bodyEl = document.getElementById("detail-body");
    bodyEl.innerHTML = html;
    rewriteInternalLinks(bodyEl);

    const bl = backlinks[conceptId] || [];
    const blSection = document.getElementById("detail-backlinks");
    const blList = document.getElementById("backlinks-list");
    blList.innerHTML = "";
    if (bl.length) {
      blSection.hidden = false;
      for (const src of bl) {
        const li = document.createElement("li");
        const a = document.createElement("a");
        a.textContent = nodeIndex[src]?.label || src;
        a.dataset.target = src;
        a.addEventListener("click", () => showDetail(src));
        li.appendChild(a);
        const muted = document.createElement("span");
        muted.className = "muted";
        muted.textContent = ` (${src})`;
        li.appendChild(muted);
        blList.appendChild(li);
      }
    } else {
      blSection.hidden = true;
    }

    cy.animate({ center: { eles: node }, zoom: Math.max(cy.zoom(), 1.0) }, { duration: 200 });
  }

  function makeBadge(text, cls) {
    const span = document.createElement("span");
    span.className = "badge " + cls;
    span.textContent = text;
    return span;
  }

  function formatActorEvent(event) {
    if (!event || !event.by) return "—";
    return event.at ? `${event.by} · ${event.at}` : String(event.by);
  }

  function rewriteInternalLinks(root) {
    root.querySelectorAll("a[href]").forEach((a) => {
      const href = a.getAttribute("href");
      if (!href) return;
      if (href.startsWith("/") && href.endsWith(".md")) {
        const target = href.slice(1, -3);
        if (nodeIndex[target]) {
          a.className = "internal";
          a.setAttribute("href", "javascript:void(0)");
          a.addEventListener("click", (e) => {
            e.preventDefault();
            showDetail(target);
          });
          return;
        }
      }
      a.className = "external";
      a.setAttribute("target", "_blank");
      a.setAttribute("rel", "noopener");
    });
  }

  // === Resizable sidebar ===
  const resizerEl = document.getElementById('resizer');
  const graphEl = document.getElementById('graph');
  const detailEl = document.getElementById('detail');

  if (resizerEl && graphEl && detailEl) {
    let isDragging = false;

    const STORAGE_KEY = 'viz-sidebar-width';

    function applyWidth(pxWidth) {
      const min = 220;
      const max = Math.max(min, window.innerWidth * 0.65);
      const w = Math.max(min, Math.min(max, pxWidth));
      detailEl.style.width = `${w}px`;
      graphEl.style.flex = '1 1 0';
    }

    function saveWidth() {
      try { localStorage.setItem(STORAGE_KEY, detailEl.style.width); } catch (_) {}
    }

    function loadWidth() {
      try {
        const saved = localStorage.getItem(STORAGE_KEY);
        if (saved) {
          applyWidth(parseFloat(saved));
        } else {
          // default from CSS (40%)
          // ensure graph takes the rest
          graphEl.style.flex = '1 1 0';
        }
      } catch (_) {
        graphEl.style.flex = '1 1 0';
      }
    }

    // Initialize
    loadWidth();

    resizerEl.addEventListener('mousedown', (e) => {
      isDragging = true;
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';
      e.preventDefault();
    });

    document.addEventListener('mousemove', (e) => {
      if (!isDragging) return;
      const container = detailEl.parentElement.getBoundingClientRect();
      const newDetailWidth = container.right - e.clientX;
      applyWidth(newDetailWidth);
    });

    document.addEventListener('mouseup', () => {
      if (isDragging) {
        isDragging = false;
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        saveWidth();
      }
    });

    // Double-click resizer to reset
    resizerEl.addEventListener('dblclick', () => {
      detailEl.style.width = '40%';
      graphEl.style.flex = '1 1 0';
      try { localStorage.removeItem(STORAGE_KEY); } catch (_) {}
    });

    // Keep proportions reasonable on window resize
    window.addEventListener('resize', () => {
      if (detailEl.style.width && detailEl.style.width.endsWith('px')) {
        applyWidth(parseFloat(detailEl.style.width));
      }
    });
  }

  // Initial selection: prefer a valid hash fragment (#node-id), else default to a dataset or first node.
  // Hash support enables shareable links that open with a specific node pre-selected.
  const hashId = getIdFromHash();
  if (hashId && nodeIndex[hashId]) {
    showDetail(hashId);
  } else {
    // Clean up any invalid/empty hash on load
    if (location.hash) {
      try { history.replaceState(null, "", location.pathname + location.search); } catch (_) {}
    }
    const initial =
      bundle.nodes.find((n) => n.data.type === "BigQuery Dataset") ||
      bundle.nodes[0];
    if (initial) showDetail(initial.data.id);
  }

  // Support browser back/forward and direct hash edits
  window.addEventListener("hashchange", () => {
    const id = getIdFromHash();
    if (id && nodeIndex[id]) {
      showDetail(id);
    } else {
      clearSelection();
    }
  });
})();
