(() => {
  const MIN_TEXT = 600;
  const ARTICLE_TEXT_SHARE = 0.85;

  const ARTICLE_BODY = "[itemprop~='articleBody'], .entry-content, .post-content";
  const ARTICLE = "article, .post, .article";
  const MAIN = "main, [role='main'], #content, .content";

  const EXCLUDED = [
    "script", "style", "noscript", "template", "iframe", "object", "embed",
    "form", "button", "input", "select", "textarea", "nav", "aside", "footer",
    "dialog", "svg", "canvas", "video", "audio", "[hidden]", "[aria-hidden='true']",
    "[role='navigation']", "[role='complementary']",
    ".ad", ".ads", ".advert", ".share", ".social", ".related", ".comments"
  ].join(", ");

  const ALLOWED = new Set((
    "p h1 h2 h3 h4 h5 h6 ul ol li blockquote pre code em strong b i a img " +
    "figure figcaption table caption thead tbody tfoot tr th td br hr sup sub " +
    "div section article span dl dt dd"
  ).split(" "));

  const body = document.body;
  if (!body) return "no-article";

  const normalize = text => (text ?? "").replace(/\s+/g, " ").trim();

  const isVisible = element => {
    const style = getComputedStyle(element);
    return style.display !== "none" && style.visibility === "visible";
  };

  const scores = new Map();

  for (const paragraph of body.querySelectorAll("p")) {
    if (
      paragraph.closest(EXCLUDED) ||
      !paragraph.getClientRects().length ||
      !isVisible(paragraph)
    ) continue;

    const textLength = normalize(paragraph.innerText).length;
    const linkedLength = Array.from(paragraph.querySelectorAll("a"))
      .reduce((sum, link) => sum + normalize(link.innerText).length, 0);
    const score = Math.max(0, textLength - linkedLength);
    if (!score) continue;

    for (let element = paragraph.parentElement; element; element = element.parentElement) {
      scores.set(element, (scores.get(element) ?? 0) + score);
      if (element === body) break;
    }
  }

  const priority = element => {
    if (element.matches(ARTICLE_BODY)) return 3;
    if (element.matches(ARTICLE)) return 2;
    if (element.matches(MAIN)) return 1;
    return 0;
  };

  const ranked = Array.from(scores, ([element, score]) => ({
    element,
    score,
    priority: priority(element)
  })).sort((a, b) => b.score - a.score);

  let best = ranked[0];
  if (!best || best.score < MIN_TEXT) return "no-article";

  const threshold = Math.max(MIN_TEXT, best.score * ARTICLE_TEXT_SHARE);

  for (const candidate of ranked) {
    if (candidate.score < threshold) break;

    const higherPriority = candidate.priority > best.priority;
    const narrowerMatch =
      candidate.priority === best.priority &&
      best.element.contains(candidate.element);

    if (higherPriority || narrowerMatch) best = candidate;
  }

  const reader = document.implementation.createHTMLDocument();
  const baseUrl = document.baseURI;

  const safeUrl = value => {
    if (!value?.trim()) return null;

    try {
      const url = new URL(value, baseUrl);
      return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
    } catch {
      return null;
    }
  };

  const copyContent = (source, destination) => {
    for (const node of source.childNodes) {
      if (node.nodeType === Node.TEXT_NODE) {
        destination.append(reader.createTextNode(node.data));
        continue;
      }

      if (
        node.nodeType !== Node.ELEMENT_NODE ||
        node.namespaceURI !== "http://www.w3.org/1999/xhtml" ||
        node.matches(EXCLUDED) ||
        !isVisible(node)
      ) continue;

      const tag = node.localName;
      if (!ALLOWED.has(tag)) {
        copyContent(node, destination);
        continue;
      }

      const element = reader.createElement(tag);

      if (tag === "a") {
        const href = safeUrl(node.getAttribute("href"));
        if (href) {
          element.href = href;
          element.target = "_blank";
          element.rel = "noopener noreferrer";
        }
      }

      if (tag === "img") {
        const src = [
          node.getAttribute("data-src"),
          node.getAttribute("data-original"),
          node.currentSrc,
          node.getAttribute("src")
        ].map(safeUrl).find(Boolean);

        if (!src) {
          destination.append(reader.createTextNode(node.alt));
          continue;
        }

        element.alt = node.alt;
        element.loading = "lazy";
        element.referrerPolicy = "no-referrer";
        element.src = src;
      }

      if (tag === "td" || tag === "th") {
        for (const name of ["colspan", "rowspan"]) {
          const value = node.getAttribute(name);
          if (value !== null && /^\d+$/.test(value)) {
            element.setAttribute(name, value);
          }
        }
      }

      copyContent(node, element);
      destination.append(element);
    }
  };

  const content = reader.createElement("article");
  copyContent(best.element, content);

  const retainedText = Array.from(content.querySelectorAll("p"))
    .reduce((sum, paragraph) => sum + normalize(paragraph.textContent).length, 0);

  if (retainedText < MIN_TEXT) return "no-article";

  const articleHeading = content.querySelector("h1");
  const enclosingArticle = best.element.closest(ARTICLE);
  const heading =
    normalize(articleHeading?.textContent) ||
    normalize(enclosingArticle?.querySelector("h1")?.innerText) ||
    normalize(document.title) ||
    location.hostname;

  if (articleHeading && normalize(articleHeading.textContent) === heading) {
    articleHeading.remove();
  }

  reader.title = heading;

  const viewport = reader.createElement("meta");
  viewport.name = "viewport";
  viewport.content = "width=device-width, initial-scale=1";

  const style = reader.createElement("style");
  style.textContent = `
    :root { color-scheme: light dark; }
    body {
      margin: 0;
      background: Canvas;
      color: CanvasText;
      font: 19px/1.65 Georgia, "Times New Roman", serif;
    }
    main { max-width: 680px; margin: auto; padding: 48px 24px 96px; overflow-wrap: anywhere; }
    .site { font: 14px/1.4 system-ui, sans-serif; opacity: .7; }
    h1, h2, h3, h4 { font-family: system-ui, sans-serif; line-height: 1.25; }
    img { display: block; max-width: 100%; height: auto; margin: 1.2em auto; }
    figcaption { font-size: 14px; text-align: center; }
    a { color: LinkText; }
    pre, table { max-width: 100%; overflow: auto; }
    pre { padding: 12px; border: 1px solid GrayText; }
    blockquote { margin: 1em 0; padding-inline-start: 16px; border-inline-start: 3px solid GrayText; }
    table { display: block; border-collapse: collapse; font-size: 15px; }
    td, th { border: 1px solid GrayText; padding: 4px 8px; }
  `;
  reader.head.append(viewport, style);

  const main = reader.createElement("main");
  const site = reader.createElement("div");
  site.className = "site";
  site.textContent = location.hostname.replace(/^www\./i, "");

  const title = reader.createElement("h1");
  title.textContent = heading;
  main.append(site, title, content);
  reader.body.append(main);

  window.stop();
  document.documentElement.replaceChildren(reader.head, reader.body);
  window.scrollTo(0, 0);
  return "ok";
})();