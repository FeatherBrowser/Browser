const DATA = {
  history: __HISTORY_JSON__,
  downloads: __DOWNLOADS_JSON__,
  favorites: __BOOKMARKS_JSON__,
};

const initialSection = __SECTION_JSON__;

const $ = (id) => document.getElementById(id);

const post = (action, extra = {}) => {
  window.chrome.webview.postMessage({ action, ...extra });
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

  const items = DATA.history.filter((x) => matches(x.Title, x.Url));

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
        x.Title || x.Url,
        `${host(x.Url)} · ${fmtDate(x.LastVisited)} · ${x.VisitCount || 1} visit${x.VisitCount === 1 ? "" : "s"}`,
        () => {
          post("open-url", { url: x.Url });
        },
        [
          makeButton("Open", "open-url", { url: x.Url }),
          makeButton("Remove", "remove-history", { url: x.Url }, true),
        ],
      ),
    );
  });
}

function renderDownloads() {
  const root = $("downloadRows");
  root.replaceChildren();

  const items = DATA.downloads.filter((x) =>
    matches(x.FileName, x.FilePath, x.SourceUrl, x.State),
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
    const state = x.State || "Unknown";
    const buttons = [];

    if (x.FilePath) {
      buttons.push(
        makeButton(
          state === "Completed" ? "Open" : "Show",
          "open-download-file",
          { path: x.FilePath },
        ),
      );
    }

    buttons.push(makeButton("Remove", "remove-download", { id: x.Id }, true));

    root.appendChild(
      makeRow(
        x.FileName || "Download",
        `${state} · ${fmtDate(x.CompletedAt || x.StartedAt)}${x.SourceUrl ? ` · ${host(x.SourceUrl)}` : ""}`,
        x.FilePath
          ? () => {
              post("open-download-file", {
                path: x.FilePath,
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

  const items = DATA.favorites.filter((x) => matches(x.Title, x.Url, x.Folder));

  if (!items.length) {
    root.appendChild(
      empty(query ? "No favorites match your search." : "No favorites yet."),
    );

    return;
  }

  items.slice(0, 700).forEach((x) => {
    root.appendChild(
      makeRow(
        x.Title || x.Url,
        `${x.Folder || "Favorites"} · ${host(x.Url)}`,
        () => {
          post("open-url", { url: x.Url });
        },
        [
          makeButton("Open", "open-url", { url: x.Url }),
          makeButton("Remove", "remove-bookmark", { url: x.Url }, true),
        ],
      ),
    );
  });
}

function show(next) {
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
    show(b.dataset.section);
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
