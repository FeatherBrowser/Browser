(() => {
  const MIN_TEXT = 600;
  const textLength = (el) => (el.innerText || el.textContent || "").trim().length;

  const candidates = [
    ...document.querySelectorAll("article, main, [role='main'], [itemprop='articleBody'], .post, .article, .entry-content, #content, .content")
  ];
  if (!candidates.length) candidates.push(document.body);
  const score = (el) => [...el.querySelectorAll("p")].reduce((sum, p) => sum + textLength(p), 0);
  let best = candidates
    .filter((el) => el && textLength(el) > 0)
    .sort((a, b) => score(b) - score(a))[0];

  if (best === document.body || !best || score(best) < MIN_TEXT) {
    const holders = new Map();
    for (const p of document.querySelectorAll("p")) {
      const parent = p.parentElement;
      if (parent) holders.set(parent, (holders.get(parent) || 0) + textLength(p));
    }
    let top = null;
    let topScore = 0;
    for (const [el, value] of holders) if (value > topScore) { top = el; topScore = value; }
    if (top && topScore >= MIN_TEXT) best = top;
  }

  if (!best || score(best) < MIN_TEXT) return "no-article";

  const article = best.cloneNode(true);
  article.querySelectorAll(
    "script, style, noscript, iframe, object, embed, form, button, input, select, textarea, nav, aside, footer, " +
    "svg, canvas, video, audio, [aria-hidden='true'], [hidden], .ad, .ads, .advert, .share, .social, .related, .comments"
  ).forEach((el) => el.remove());

  const allowed = new Set(["P", "H1", "H2", "H3", "H4", "H5", "H6", "UL", "OL", "LI", "BLOCKQUOTE", "PRE", "CODE",
    "EM", "STRONG", "B", "I", "A", "IMG", "FIGURE", "FIGCAPTION", "TABLE", "THEAD", "TBODY", "TR", "TH", "TD",
    "BR", "HR", "SUP", "SUB", "DIV", "SECTION", "ARTICLE", "SPAN", "PICTURE", "DL", "DT", "DD"]);
  const safeUrl = (value) => /^(https?:|data:image\/)/i.test(value || "");

  for (const el of [...article.querySelectorAll("*")]) {
    if (!allowed.has(el.tagName)) {
      el.replaceWith(...el.childNodes);
      continue;
    }
    for (const attribute of [...el.attributes]) {
      const keep =
        (el.tagName === "A" && attribute.name === "href" && safeUrl(el.href)) ||
        (el.tagName === "IMG" && (attribute.name === "alt" || (attribute.name === "src" && safeUrl(el.src)))) ||
        ((el.tagName === "TD" || el.tagName === "TH") && (attribute.name === "colspan" || attribute.name === "rowspan"));
      if (!keep) el.removeAttribute(attribute.name);
    }
    if (el.tagName === "A") { el.setAttribute("target", "_blank"); el.setAttribute("rel", "noopener noreferrer"); }
    if (el.tagName === "IMG" && !el.getAttribute("src")) el.remove();
  }

  const heading = document.querySelector("h1")?.innerText?.trim() || document.title;
  const site = location.hostname.replace(/^www\./, "");

  window.stop();
  document.documentElement.innerHTML =
    "<head><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title></title><style>" +
    ":root{color-scheme:light dark}body{margin:0;background:Canvas;color:CanvasText;font:19px/1.65 Georgia,'Times New Roman',serif}" +
    "main{max-width:680px;margin:0 auto;padding:48px 24px 96px}" +
    ".site{font:13px/1.4 'Segoe UI',system-ui,sans-serif;opacity:.65;margin-bottom:8px}" +
    "h1{font:700 32px/1.25 'Segoe UI',system-ui,sans-serif;margin:0 0 28px}" +
    "h2,h3,h4{font-family:'Segoe UI',system-ui,sans-serif;line-height:1.3;margin:1.6em 0 .6em}" +
    "img{max-width:100%;height:auto;display:block;margin:1.2em auto}figcaption{font-size:14px;opacity:.7;text-align:center}" +
    "a{color:LinkText}pre{overflow:auto;font-size:14px;padding:12px;border:1px solid GrayText}" +
    "blockquote{margin:1em 0;padding-left:16px;border-left:3px solid GrayText;opacity:.85}" +
    "table{border-collapse:collapse;font-size:15px}td,th{border:1px solid GrayText;padding:4px 8px}" +
    "</style></head><body><main><div class='site'></div><h1></h1></main></body>";
  document.title = heading;
  document.querySelector(".site").textContent = site;
  document.querySelector("h1").textContent = heading;
  const first = article.querySelector("h1");
  if (first && first.textContent.trim() === heading) first.remove();
  document.querySelector("main").appendChild(article);
  window.scrollTo(0, 0);
  return "ok";
})();
