const DATA = {
  history: __HISTORY_JSON__,
  downloads: __DOWNLOADS_JSON__,
  favorites: __BOOKMARKS_JSON__,
};

const initialSection = __SECTION_JSON__;
const featherToken = __feather_token_json__;

const $ = (id) => document.getElementById(id);

const post = (action, extra = {}) => {
  window.chrome.webview.postMessage({ action, ...extra, __featherToken: featherToken });
};

const escapeText = (v) => String(v ?? "");

const fmtDate = (v) => {
  try {
    return new Date(v).toLocaleString();
  } catch {
    return "";
  }
};

const host = (url) => {
  try {
    return new URL(url).hostname;
  } catch {
    return url || "";
  }
};

let section = ["history", "downloads", "favorites"].includes(initialSection)
  ? initialSection
  : "history";

let query = "";

function makeButton(text, action, extra = {}, danger = false) {
  const b = document.createElement("button");

  b.className = "btn" + (danger ? " danger" : "");
  b.textContent = text;

  b.addEventListener("click", (e) => {
    e.stopPropagation();
    post(action, extra);
  });

  return b;
}

function makeRow(title, meta, onOpen, buttons = []) {
  const row = document.createElement("div");
  row.className = "row";

  const left = document.createElement("div");

  const t = document.createElement("div");
  t.className = "title";
  t.textContent = title || "Untitled";

  const m = document.createElement("div");
  m.className = "meta";
  m.textContent = meta || "";

  left.append(t, m);

  if (onOpen) {
    row.style.cursor = "pointer";
    row.addEventListener("click", onOpen);
  }

  const acts = document.createElement("div");
  acts.className = "actions";

  buttons.forEach((x) => {
    acts.appendChild(x);
  });

  row.append(left, acts);

  return row;
}

function empty(text) {
  const e = document.createElement("div");
  e.className = "empty";
  e.textContent = text;

  return e;
}

function matches(...values) {
  if (!query) {
    return true;
  }

  const q = query.toLowerCase();

  return values.some((v) =>
    String(v ?? "")
      .toLowerCase()
      .includes(q),
  );
}

function renderHistory() {
  const root = $("historyRows");
  root.replaceChildren();

  const items = DATA.history.filter((x) => matches(x.title, x.url));

  if (!items.length) {
    root.appendChild(
      empty(
        query ? "No history matches your search." : "No browsing history yet.",
      ),
    );

    return;
  }

  items.slice(0, 700).forEach((x) => {
    root.appendChild(
      makeRow(
        x.title || x.url,
        `${host(x.url)} · ${fmtDate(x.lastVisited)} · ${x.visitCount || 1} visit${x.visitCount === 1 ? "" : "s"}`,
        () => {
          post("open-url", { url: x.url });
        },
        [
          makeButton("Open", "open-url", { url: x.url }),
          makeButton("Remove", "remove-history", { url: x.url }, true),
        ],
      ),
    );
  });
}

function renderDownloads() {
  const root = $("downloadRows");
  root.replaceChildren();

  const items = DATA.downloads.filter((x) =>
    matches(x.fileName, x.filePath, x.sourceUrl, x.state),
  );

  if (!items.length) {
    root.appendChild(
      empty(
        query
          ? "No downloads match your search."
          : "No downloads recorded yet.",
      ),
    );

    return;
  }

  items.slice(0, 500).forEach((x) => {
    const state = x.state || "Unknown";
    const buttons = [];

    if (x.filePath) {
      buttons.push(
        makeButton(
          state === "Completed" ? "Open" : "Show",
          "open-download-file",
          { id: x.id },
        ),
      );
    }

    buttons.push(makeButton("Remove", "remove-download", { id: x.id }, true));

    root.appendChild(
      makeRow(
        x.fileName || "Download",
        `${state} · ${fmtDate(x.completedAt || x.startedAt)}${x.sourceUrl ? ` · ${host(x.sourceUrl)}` : ""}`,
        x.filePath
          ? () => {
              post("open-download-file", {
                id: x.id,
              });
            }
          : null,
        buttons,
      ),
    );
  });
}

function renderFavorites() {
  const root = $("favoriteRows");
  root.replaceChildren();

  const items = DATA.favorites.filter((x) => matches(x.title, x.url, x.folder));

  if (!items.length) {
    root.appendChild(
      empty(query ? "No favorites match your search." : "No favorites yet."),
    );

    return;
  }

  items.slice(0, 700).forEach((x) => {
    root.appendChild(
      makeRow(
        x.title || x.url,
        `${x.folder || "Favorites"} · ${host(x.url)}`,
        () => {
          post("open-url", { url: x.url });
        },
        [
          makeButton("Open", "open-url", { url: x.url }),
          makeButton("Remove", "remove-bookmark", { url: x.url }, true),
        ],
      ),
    );
  });
}

function show(next, notifyHost = false) {
  section = next;

  document.querySelectorAll("nav button,.section").forEach((x) => {
    x.classList.remove("active");
  });

  document
    .querySelector(`nav button[data-section="${section}"]`)
    ?.classList.add("active");

  $(section).classList.add("active");

  const headings = {
    history: ["History", "Recently visited pages stored locally by Feather."],
    downloads: ["Downloads", "Files downloaded through Feather."],
    favorites: ["Favorites", "Saved pages and imported Edge favorites."],
  };

  $("heading").textContent = headings[section][0];
  $("hint").textContent = headings[section][1];

  render();

  if (notifyHost) {
    post("library-section-changed", { section });
  }
}

function render() {
  $("historyCount").textContent = DATA.history.length;
  $("downloadCount").textContent = DATA.downloads.length;
  $("favoriteCount").textContent = DATA.favorites.length;

  if (section === "history") {
    renderHistory();
  } else if (section === "downloads") {
    renderDownloads();
  } else {
    renderFavorites();
  }
}

document.querySelectorAll("nav button").forEach((b) => {
  b.addEventListener("click", () => {
    show(b.dataset.section, true);
  });
});

document.querySelectorAll("[data-action]").forEach((b) => {
  b.addEventListener("click", () => {
    post(b.dataset.action);
  });
});

$("search").addEventListener("input", (e) => {
  query = e.target.value.trim();
  render();
});

show(section);
