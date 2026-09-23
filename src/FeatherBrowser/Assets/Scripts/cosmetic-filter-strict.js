(() => {
  if (document.getElementById('__feather_blocker_style')) return;
  const style = document.createElement('style');
  style.id = '__feather_blocker_style';
  style.textContent = `
    ins.adsbygoogle,
    [id^="google_ads_"], [id^="div-gpt-ad"], [id*="-ad-slot"], [id*="sponsor" i],
    [class~="ad-container"], [class~="ads-container"], [class~="advertisement"],
    [class^="ad-slot"], [class*=" ad-slot"], [class*="ad-banner" i], [class*="ad-wrapper" i], [class*="ad-unit" i], [class*=" sponsored" i], [class^="sponsored" i], [class*="promoted" i],
    [aria-label="advertisement" i], [aria-label="sponsored" i], [data-ad], [data-ad-slot], [data-ad-unit],
    [data-testid*="ad-" i], [data-testid*="sponsor" i], [data-component*="advert" i],
    iframe[src*="doubleclick.net"], iframe[src*="googlesyndication.com"], iframe[src*="taboola"],
    iframe[src*="outbrain"], iframe[src*="amazon-adsystem"], iframe[src*="adnxs"] {
      display:none !important; visibility:hidden !important; min-height:0 !important; height:0 !important;
      max-height:0 !important; margin:0 !important; padding:0 !important; border:0 !important;
    }
  `;
  (document.head || document.documentElement).appendChild(style);

  const adSelectors = '[data-ad],[data-ad-slot],[aria-label="Advertisement"],[aria-label="Sponsored"],ins.adsbygoogle,[data-testid*="ad-" i]';
  const sweep = () => {
    for (const node of document.querySelectorAll(adSelectors)) {
      node.style.setProperty('display','none','important');
      node.style.setProperty('height','0','important');
      node.style.setProperty('min-height','0','important');
    }
  };
  sweep();
  let queued = false;
  const observer = new MutationObserver(() => {
    if (queued) return;
    queued = true;
    requestAnimationFrame(() => { queued = false; sweep(); });
  });
  observer.observe(document.documentElement, { childList:true, subtree:true });
  setTimeout(() => observer.disconnect(), 20000);
})();