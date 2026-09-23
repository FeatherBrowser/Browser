(() => {
  if (document.getElementById('__feather_blocker_style')) return;
  const style = document.createElement('style');
  style.id = '__feather_blocker_style';
  style.textContent = `
    ins.adsbygoogle,
    [id^="google_ads_"], [id^="div-gpt-ad"], [id*="-ad-slot"],
    [class~="ad-container"], [class~="ads-container"], [class~="advertisement"],
    [class^="ad-slot"], [class*=" ad-slot"], [class*="ad-banner" i], [class*="ad-wrapper" i], [class*="ad-unit" i], [data-ad], [data-ad-slot], [data-ad-unit],
    iframe[src*="doubleclick.net"], iframe[src*="googlesyndication.com"],
    iframe[src*="taboola"], iframe[src*="outbrain"], iframe[src*="amazon-adsystem"] {
      display:none !important; visibility:hidden !important; min-height:0 !important; height:0 !important;
    }
  `;
  (document.head || document.documentElement).appendChild(style);
})();