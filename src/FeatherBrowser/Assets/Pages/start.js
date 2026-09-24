let links = __QUICK_LINKS_JSON__;
const initialState = __HOME_STATE_JSON__;
const bridge = window.chrome?.webview;

const send = (action, data = {}) =>
    bridge?.postMessage({ action, ...data });

let editing = false;
let selected = -1;
let recentItems = [];
let favicons = {};
let toastTimer;

const dialog = document.getElementById("shortcut-dialog");
const nameInput = document.getElementById("shortcut-name");
const urlInput = document.getElementById("shortcut-url");
const error = document.getElementById("shortcut-error");
const customizePanel = document.getElementById("customize-panel");
const customizeButton = document.getElementById("customize-button");

function toast(message) {
    const target = document.getElementById("toast");

    target.textContent = message;
    target.classList.add("show");

    clearTimeout(toastTimer);

    toastTimer = setTimeout(() => {
        target.classList.remove("show");
    }, 2200);
}

function hostFor(url) {
    try {
        return new URL(url).hostname.replace(/^www\./i, "");
    } catch {
        return "";
    }
}

function faviconFor(url) {
    const source = Object.hasOwn(favicons, url) ? favicons[url] : "";

    return typeof source === "string" &&
        source.startsWith("data:image/png;base64,")
        ? source
        : "";
}

function makeIcon(url, label, className) {
    const icon = document.createElement("span");
    icon.className = className;

    const fallback = document.createElement("span");
    fallback.className = "fallback-icon";
    fallback.textContent = (label || hostFor(url) || "?")
        .trim()
        .slice(0, 1)
        .toUpperCase();

    const source = faviconFor(url);

    if (!source) {
        icon.append(fallback);
        return icon;
    }

    const image = document.createElement("img");

    image.alt = "";
    image.decoding = "async";
    image.src = source;

    image.addEventListener(
        "error",
        () => image.replaceWith(fallback),
        { once: true }
    );

    icon.append(image);

    return icon;
}

document.getElementById("search").addEventListener("submit", event => {
    event.preventDefault();

    const value = document.getElementById("q").value.trim();

    if (value) {
        send("home-navigate", {
            url: value
        });
    }
});

document.querySelectorAll("[data-action]").forEach(button => {
    button.addEventListener("click", () => {
        send(button.dataset.action);
        closeCustomize();
    });
});

function renderState(state) {
  if (state.favicons && typeof state.favicons === "object") {
      const changed =
          JSON.stringify(favicons) !== JSON.stringify(state.favicons);

      favicons = state.favicons;

      if (changed) {
          renderLinks();
      }
    }
    for (const [id, key] of [
        ["memory-toggle", "memorySaver"],
        ["shield-toggle", "shield"]
    ]) {
        const element = document.getElementById(id);

        if (element) {
            element.setAttribute(
                "aria-checked",
                String(Boolean(state[key]))
            );
        }
    }

    if (Array.isArray(state.recent)) {
        recentItems = state.recent;
        renderRecent();
    }
}

function renderLinks() {
    const container = document.getElementById("quick-links");

    container.replaceChildren();

    links.forEach((link, index) => {
        const button = document.createElement("button");

        button.type = "button";
        button.className = `shortcut${editing ? " editing" : ""}`;
        button.title = editing
            ? `Edit ${link.Name}`
            : link.Url;

        const icon = makeIcon(
            link.Url,
            link.Name,
            "site-icon"
            image.alt = "";
            image.decoding = "async";
            image.addEventListener("error", () => image.replaceWith(fallback), { once: true }); 
            image.src = source;
            icon.append(image);
        );

        const label = document.createElement("span");

        label.className = "shortcut-label";
        label.textContent = link.Name;

        button.append(icon, label);

        button.addEventListener("click", () => {
            if (editing) {
                openEditor(index);
                return;
            }

            send("home-navigate", {
                url: link.Url
            });
        });

        container.append(button);
    });

    if (links.length < 12) {
        const add = document.createElement("button");

        add.className = "add-link";
        add.type = "button";
        add.title = "Add shortcut";

        const plus = document.createElement("span");

        plus.className = "plus";
        plus.textContent = "+";

        const label = document.createElement("span");

        label.className = "shortcut-label";
        label.textContent = "Add shortcut";

        add.append(plus, label);

        add.addEventListener("click", () => {
            openEditor(-1);
        });

        container.append(add);
    }
}

function renderRecent() {
    const container = document.getElementById("recent-grid");

    container.replaceChildren();

    if (!recentItems.length) {
        const empty = document.createElement("div");

        empty.className = "recent-placeholder";
        empty.textContent = "Your recent pages will show up here.";

        container.append(empty);

        return;
    }

    recentItems
        .slice(0, 2)
        .forEach(item => {
            const card = document.createElement("button");

            card.type = "button";
            card.className = "recent-card";
            card.title = item.url;

            card.addEventListener("click", () => {
                send("home-navigate", {
                    url: item.url
                });
            });

            const icon = makeIcon(
                item.url,
                item.title,
                "recent-icon"
            );

            const copy = document.createElement("span");
            copy.className = "recent-copy";

            const title = document.createElement("span");

            title.className = "recent-title";
            title.textContent =
                item.title ||
                item.host ||
                "Recent page";

            const host = document.createElement("span");

            host.className = "recent-host";
            host.textContent =
                item.host ||
                hostFor(item.url);

            copy.append(title, host);

            const more = document.createElement("button");

            more.type = "button";
            more.className = "recent-more";
            more.textContent = "⋮";

            more.setAttribute(
                "aria-label",
                `Options for ${title.textContent}`
            );

            const menu = document.createElement("div");

            menu.className = "recent-menu";
            menu.hidden = true;

            const remove = document.createElement("button");

            remove.type = "button";
            remove.textContent = "Remove from history";

            remove.addEventListener("click", event => {
                event.stopPropagation();

                send("remove-history", {
                    url: item.url
                });

                recentItems = recentItems.filter(
                    entry => entry.url !== item.url
                );

                renderRecent();
                toast("Removed from history");
            });

            menu.append(remove);

            more.addEventListener("click", event => {
                event.stopPropagation();

                document
                    .querySelectorAll(".recent-menu")
                    .forEach(other => {
                        if (other !== menu) {
                            other.hidden = true;
                        }
                    });

                menu.hidden = !menu.hidden;
            });

            menu.addEventListener("click", event => {
                event.stopPropagation();
            });

            card.append(
                icon,
                copy,
                more,
                menu
            );

            container.append(card);
        });
}

function openEditor(index) {
    selected = index;
    error.textContent = "";

    document.getElementById("dialog-title").textContent =
        index < 0
            ? "Add shortcut"
            : "Edit shortcut";

    document.getElementById("delete-link").hidden =
        index < 0;

    nameInput.value =
        index < 0
            ? ""
            : links[index].Name;

    urlInput.value =
        index < 0
            ? ""
            : links[index].Url;

    closeCustomize();

    dialog.showModal();
    nameInput.focus();
}

function saveLinks(next) {
    links = next;

    renderLinks();

    send("save-home-links", {
        links: next
    });

    dialog.close();
}

function setEditing(value) {
    editing = value;

    document
        .getElementById("edit-links")
        .querySelector("span:last-child")
        .textContent =
            editing
                ? "Done editing"
                : "Edit shortcuts";

    renderLinks();

    if (editing) {
        toast("Select a shortcut to edit it");
    }
}

function openCustomize() {
    customizePanel.hidden = false;

    customizeButton.setAttribute(
        "aria-expanded",
        "true"
    );
}

function closeCustomize() {
    customizePanel.hidden = true;

    customizeButton.setAttribute(
        "aria-expanded",
        "false"
    );
}

customizeButton.addEventListener("click", event => {
    event.stopPropagation();

    if (customizePanel.hidden) {
        openCustomize();
    } else {
        closeCustomize();
    }
});

customizePanel.addEventListener("click", event => {
    event.stopPropagation();
});

document.addEventListener("click", () => {
    closeCustomize();

    document
        .querySelectorAll(".recent-menu")
        .forEach(menu => {
            menu.hidden = true;
        });
});

document
    .getElementById("edit-links")
    .addEventListener("click", () => {
        setEditing(!editing);
        closeCustomize();
    });

document
    .getElementById("history-link")
    .addEventListener("click", () => {
        send("open-history-library");
    });

document
    .getElementById("cancel-link")
    .addEventListener("click", () => {
        dialog.close();
    });

document
    .getElementById("close-dialog")
    .addEventListener("click", () => {
        dialog.close();
    });

document
    .getElementById("delete-link")
    .addEventListener("click", () => {
        if (selected < 0) {
            return;
        }

        saveLinks(
            links.filter(
                (_, index) => index !== selected
            )
        );
    });

document
    .getElementById("shortcut-form")
    .addEventListener("submit", event => {
        event.preventDefault();

        const name = nameInput.value.trim();
        let raw = urlInput.value.trim();

        if (!/^[a-z][a-z\d+.-]*:/i.test(raw)) {
            raw = `https://${raw}`;
        }

        let url;

        try {
            url = new URL(raw);

            if (
                !["https:", "http:"].includes(url.protocol) ||
                !url.hostname ||
                url.username ||
                url.password
            ) {
                throw new Error();
            }
        } catch {
            error.textContent =
                "Enter a valid HTTP or HTTPS website.";

            return;
        }

        if (!name) {
            error.textContent =
                "Give this shortcut a name.";

            return;
        }

        const next = [...links];

        const link = {
            Name: name,
            Url: url.href
        };

        if (selected < 0) {
            next.push(link);
        } else {
            next[selected] = link;
        }

        saveLinks(next);
    });

document.addEventListener("keydown", event => {
    if (
        event.ctrlKey &&
        event.key.toLowerCase() === "k"
    ) {
        event.preventDefault();
        send("open-command-palette");
        return;
    }

    if (
        event.key === "/" &&
        document.activeElement?.tagName !== "INPUT" &&
        !dialog.open
    ) {
        event.preventDefault();

        document
            .getElementById("q")
            .focus();
    }

    if (event.key === "Escape") {
        closeCustomize();

        if (editing) {
            setEditing(false);
        }
    }
});

bridge?.addEventListener("message", event => {
    if (event.data?.type === "home-stats") {
        renderState(event.data);
    }

    if (event.data?.type === "home-links") {
        links = event.data.links ?? [];
        renderLinks();
    }
});

renderLinks();
renderState(initialState);
send("home-ready");
