const state = {
  leads: [],
  defaults: null
};

const elements = {
  form: document.getElementById("search-form"),
  statusText: document.getElementById("status-text"),
  runButton: document.getElementById("run-button"),
  summaryCards: document.getElementById("summary-cards"),
  marketsGrid: document.getElementById("markets-grid"),
  depthGrid: document.getElementById("depth-grid"),
  marketTemplate: document.getElementById("market-template"),
  depthTemplate: document.getElementById("depth-template"),
  resultsBody: document.getElementById("results-body"),
  resultsShell: document.getElementById("results-table-shell"),
  resultsToolbar: document.getElementById("results-toolbar"),
  resultsFilter: document.getElementById("results-filter"),
  downloadCsv: document.getElementById("download-csv"),
  downloadJson: document.getElementById("download-json"),
  downloadsBar: document.getElementById("downloads-bar"),
  pathsNote: document.getElementById("paths-note"),
  seedGamesBlock: document.getElementById("seed-games-block"),
  seedGames: document.getElementById("seed-games")
};

bootstrap().catch((error) => {
  setStatus(error.message || "Failed to load defaults.", true);
});

elements.form.addEventListener("submit", async (event) => {
  event.preventDefault();
  await runDiscovery();
});

elements.resultsFilter.addEventListener("input", () => {
  renderTable(filterLeads(elements.resultsFilter.value));
});

async function bootstrap() {
  const response = await fetch("/api/defaults");
  if (!response.ok) {
    throw new Error("Could not load app defaults.");
  }

  const payload = await response.json();
  const defaults = {
    defaults: payload.defaults || payload.Defaults,
    markets: (payload.markets || payload.Markets || []).map(normalizeMarket),
    depths: (payload.depths || payload.Depths || []).map(normalizeDepth)
  };

  state.defaults = defaults;
  renderMarkets(defaults.markets || [], defaults.defaults?.markets || defaults.defaults?.Markets || []);
  renderDepths(defaults.depths || [], defaults.defaults?.depth || defaults.defaults?.Depth || "balanced");
  hydrateDefaults(defaults.defaults || {});
}

function hydrateDefaults(defaults) {
  document.getElementById("published-within").value = defaults.publishedWithinMonths ?? defaults.PublishedWithinMonths ?? 24;
  document.getElementById("min-subscribers").value = defaults.minSubscribers ?? defaults.MinSubscribers ?? 1000;
  document.getElementById("max-subscribers").value = defaults.maxSubscribers ?? defaults.MaxSubscribers ?? 750000;
}

function renderMarkets(markets, selected) {
  elements.marketsGrid.innerHTML = "";

  for (const market of markets) {
    const node = elements.marketTemplate.content.firstElementChild.cloneNode(true);
    const input = node.querySelector("input");
    const title = node.querySelector(".market-pill-text");
    const meta = node.querySelector(".market-pill-meta");

    input.value = market.key;
    input.checked = selected.includes(market.key);
    title.textContent = market.name;
    meta.textContent = `${market.language} · ${market.region}`;

    elements.marketsGrid.appendChild(node);
  }
}

function renderDepths(depths, selected) {
  elements.depthGrid.innerHTML = "";

  for (const depth of depths) {
    const node = elements.depthTemplate.content.firstElementChild.cloneNode(true);
    const input = node.querySelector("input");
    const title = node.querySelector(".depth-name");
    const summary = node.querySelector(".depth-summary");

    input.value = depth.key;
    input.checked = depth.key === selected;
    title.textContent = depth.name;
    summary.textContent = depth.summary;

    elements.depthGrid.appendChild(node);
  }
}

async function runDiscovery() {
  const payload = collectPayload();
  setLoading(true);
  setStatus("Running Steam similarity search and YouTube discovery...", false);

  try {
    const response = await fetch("/api/discover", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });

    const rawBody = await response.json();
    const body = normalizeDiscoveryResponse(rawBody);
    if (!response.ok) {
      throw new Error(body.message || "Discovery failed.");
    }

    state.leads = body.leads || [];
    renderSummary(body);
    renderSeedGames(body.seedGames || []);
    renderDownloads(body);
    renderTable(state.leads);
    setStatus(`Finished. ${body.creatorLeadCount} leads ranked for ${body.gameName}.`, false);
  } catch (error) {
    setStatus(error.message || "Discovery failed.", true);
  } finally {
    setLoading(false);
  }
}

function normalizeDiscoveryResponse(payload) {
  const rawLeads = payload.leads || payload.Leads || [];

  return {
    message: payload.message || payload.Message,
    gameName: payload.gameName || payload.GameName,
    searchRequestsExecuted: payload.searchRequestsExecuted || payload.SearchRequestsExecuted || 0,
    creatorLeadCount: payload.creatorLeadCount || payload.CreatorLeadCount || 0,
    csvPath: payload.csvPath || payload.CsvPath || "",
    jsonPath: payload.jsonPath || payload.JsonPath || "",
    csvDownloadUrl: payload.csvDownloadUrl || payload.CsvDownloadUrl || "#",
    jsonDownloadUrl: payload.jsonDownloadUrl || payload.JsonDownloadUrl || "#",
    seedGames: payload.seedGames || payload.SeedGames || [],
    leads: rawLeads.map(normalizeLead)
  };
}

function normalizeLead(lead) {
  return {
    channelTitle: lead.channelTitle || lead.ChannelTitle || "",
    channelUrl: lead.channelUrl || lead.ChannelUrl || "#",
    fitScore: lead.fitScore || lead.FitScore || 0,
    matchedSeedGames: lead.matchedSeedGames || lead.MatchedSeedGames || [],
    matchedMarkets: lead.matchedMarkets || lead.MatchedMarkets || [],
    subscribers: lead.subscribers || lead.Subscribers || null,
    publicEmail: lead.publicEmail || lead.PublicEmail || "",
    sampleVideoTitles: lead.sampleVideoTitles || lead.SampleVideoTitles || [],
    fitNotes: lead.fitNotes || lead.FitNotes || []
  };
}

function normalizeMarket(market) {
  return {
    key: market.key || market.Key || "",
    name: market.name || market.Name || "",
    language: market.language || market.Language || "",
    region: market.region || market.Region || ""
  };
}

function normalizeDepth(depth) {
  return {
    key: depth.key || depth.Key || "",
    name: depth.name || depth.Name || "",
    summary: depth.summary || depth.Summary || ""
  };
}

function collectPayload() {
  const checkedMarkets = [...document.querySelectorAll('input[name="market"]:checked')].map((input) => input.value);
  const depth = document.querySelector('input[name="depth"]:checked')?.value || "balanced";

  return {
    steamInput: document.getElementById("steam-input").value.trim(),
    gameName: document.getElementById("game-name").value.trim(),
    apiKey: document.getElementById("api-key").value.trim(),
    searchTerms: document.getElementById("search-terms").value,
    similarGames: document.getElementById("similar-games").value,
    excludeTerms: document.getElementById("exclude-terms").value,
    markets: checkedMarkets,
    depth,
    publishedWithinMonths: Number(document.getElementById("published-within").value || 24),
    minSubscribers: Number(document.getElementById("min-subscribers").value || 0),
    maxSubscribers: Number(document.getElementById("max-subscribers").value || 0) || null,
    requirePublicEmail: document.getElementById("require-public-email").checked
  };
}

function renderSummary(result) {
  elements.summaryCards.classList.remove("summary-cards-empty");
  elements.summaryCards.innerHTML = `
    <div class="summary-grid">
      <article class="summary-card">
        <span>Game</span>
        <strong>${escapeHtml(result.gameName)}</strong>
      </article>
      <article class="summary-card">
        <span>Search Requests</span>
        <strong>${formatNumber(result.searchRequestsExecuted)}</strong>
      </article>
      <article class="summary-card">
        <span>Creator Leads</span>
        <strong>${formatNumber(result.creatorLeadCount)}</strong>
      </article>
      <article class="summary-card">
        <span>Seed Games Used</span>
        <strong>${formatNumber(result.seedGames.length)}</strong>
      </article>
    </div>
  `;
}

function renderSeedGames(seedGames) {
  if (!seedGames.length) {
    elements.seedGamesBlock.classList.add("hidden");
    elements.seedGames.innerHTML = "";
    return;
  }

  elements.seedGamesBlock.classList.remove("hidden");
  elements.seedGames.innerHTML = seedGames
    .map((seed) => `<span>${escapeHtml(seed)}</span>`)
    .join("");
}

function renderDownloads(result) {
  elements.downloadCsv.href = result.csvDownloadUrl;
  elements.downloadJson.href = result.jsonDownloadUrl;
  elements.pathsNote.textContent = `Saved to ${result.csvPath} and ${result.jsonPath}`;
  elements.downloadsBar.classList.remove("hidden");
  elements.resultsToolbar.classList.remove("hidden");
  elements.resultsShell.classList.remove("hidden");
}

function renderTable(leads) {
  elements.resultsBody.innerHTML = leads.map((lead) => `
    <tr>
      <td>
        <div class="creator-cell">
          <a href="${escapeAttribute(lead.channelUrl)}" target="_blank" rel="noreferrer">${escapeHtml(lead.channelTitle)}</a>
          <div class="micro-list">${lead.fitNotes.slice(0, 2).map((note) => `<span>${escapeHtml(note)}</span>`).join("")}</div>
        </div>
      </td>
      <td><span class="score-pill">${escapeHtml(String(lead.fitScore))}</span></td>
      <td><div class="micro-list">${lead.matchedSeedGames.map((item) => `<span>${escapeHtml(item)}</span>`).join("")}</div></td>
      <td><div class="micro-list">${lead.matchedMarkets.map((item) => `<span>${escapeHtml(item)}</span>`).join("")}</div></td>
      <td>${lead.subscribers ? formatNumber(lead.subscribers) : "n/a"}</td>
      <td>${lead.publicEmail ? `<a href="mailto:${escapeAttribute(lead.publicEmail)}">${escapeHtml(lead.publicEmail)}</a>` : "<span class=\"sample-title\">Not public</span>"}</td>
      <td><div class="sample-title">${escapeHtml(lead.sampleVideoTitles[0] || "No sample title")}</div></td>
    </tr>
  `).join("");
}

function filterLeads(query) {
  if (!query.trim()) {
    return state.leads;
  }

  const needle = query.trim().toLowerCase();
  return state.leads.filter((lead) => {
    const haystack = [
      lead.channelTitle,
      lead.publicEmail || "",
      ...(lead.matchedMarkets || []),
      ...(lead.matchedSeedGames || []),
      ...(lead.sampleVideoTitles || [])
    ].join(" ").toLowerCase();

    return haystack.includes(needle);
  });
}

function setLoading(isLoading) {
  elements.runButton.disabled = isLoading;
  elements.runButton.querySelector(".button-label").textContent = isLoading ? "Running Search..." : "Run Creator Search";
}

function setStatus(message, isError) {
  elements.statusText.textContent = message;
  elements.statusText.style.color = isError ? "#b42318" : "";
}

function formatNumber(value) {
  return new Intl.NumberFormat().format(value);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

function escapeAttribute(value) {
  return escapeHtml(value).replaceAll("`", "&#96;");
}
