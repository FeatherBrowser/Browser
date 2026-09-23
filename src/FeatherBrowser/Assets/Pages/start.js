const prefix = __SEARCH_PREFIX_JSON__;
let links = __QUICK_LINKS_JSON__;
const initialState = __HOME_STATE_JSON__;
const bridge = window.chrome?.webview;
const send = (action, data = {}) => bridge?.postMessage({ action, ...data });
let toastTimer;
function toast(message) {
  const target = document.getElementById('toast');
  target.textContent = message; target.classList.add('show');
  clearTimeout(toastTimer); toastTimer = setTimeout(() => target.classList.remove('show'), 2600);
}
document.getElementById('search').addEventListener('submit', e => {
  e.preventDefault(); const value = document.getElementById('q').value.trim();
  if (value) send('home-navigate', { url: value });
});
document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
  send(button.dataset.action);
  if (button.dataset.action === 'trim-memory') toast('Background memory trim requested');
}));
function renderState(state) {
  if (state.memoryLabel) document.getElementById('memory-label').textContent = state.memoryLabel;
  if (state.cpu) document.getElementById('cpu').textContent = state.cpu;
  for (const [id, key] of [['memory-toggle','memorySaver'],['shield-toggle','shield'],['game-toggle','gaming']])
    document.getElementById(id).setAttribute('aria-checked', String(Boolean(state[key])));
  if (state.memory) document.getElementById('memory').textContent = state.memory;
  for (const id of ['loaded','cold','sleeping','blocked'])
    if (Number.isFinite(state[id])) document.getElementById(id).textContent = state[id].toLocaleString();
  if (Number.isFinite(state.ratio)) document.getElementById('meter').style.strokeDasharray = `${Math.min(100, Math.max(0,state.ratio * 100))} 100`;
}
renderState(initialState);
bridge?.addEventListener('message', e => {
  if (e.data.type === 'home-stats') renderState(e.data);
  if (e.data.type === 'home-links') { links = e.data.links; renderLinks(); }
});
let editing = false, selected = -1;
const dialog = document.getElementById('shortcut-dialog');
const nameInput = document.getElementById('shortcut-name');
const urlInput = document.getElementById('shortcut-url');
const error = document.getElementById('shortcut-error');
const brandStyles = { 'youtube.com':['▶','#f83246'], 'twitch.tv':['T','#9349ee'], 'reddit.com':['●','#ff571c'], 'open.spotify.com':['≋','#21c779'], 'notion.so':['N','#172332'], 'github.com':['G','#31485d'] };
function renderLinks() {
  const container = document.getElementById('quick-links'); container.replaceChildren();
  links.forEach((link, index) => {
    const button = document.createElement('button'); button.type = 'button'; button.className = 'shortcut';
    const icon = document.createElement('span'); icon.className = 'site-icon';
    let host = ''; try { host = new URL(link.Url).hostname.replace(/^www\./,''); } catch {}
    const style = brandStyles[host] || [link.Name.slice(0,1).toUpperCase(), '#376fa4'];
    icon.textContent = style[0]; icon.style.setProperty('--site-color', style[1]);
    const label = document.createElement('span'); label.className = 'shortcut-label'; label.textContent = link.Name;
    button.title = editing ? `Edit ${link.Name}` : link.Url;
    button.append(icon,label); button.addEventListener('click', () => editing ? openEditor(index) : send('home-navigate',{url:link.Url}));
    container.append(button);
  });
  if (links.length < 12) {
    const add = document.createElement('button'); add.className = 'add-link'; add.type = 'button';
    const plus = document.createElement('span'); plus.textContent = '+'; add.append(plus,document.createTextNode('Add'));
    add.addEventListener('click', () => openEditor(-1)); container.append(add);
  }
}
function openEditor(index) {
  selected = index; error.textContent = '';
  document.getElementById('dialog-title').textContent = index < 0 ? 'Add shortcut' : 'Edit shortcut';
  document.getElementById('delete-link').hidden = index < 0;
  nameInput.value = index < 0 ? '' : links[index].Name; urlInput.value = index < 0 ? '' : links[index].Url;
  dialog.showModal(); nameInput.focus();
}
function saveLinks(next) { send('save-home-links',{links:next}); dialog.close(); }
document.getElementById('edit-links').addEventListener('click', e => {
  editing = !editing; e.currentTarget.textContent = editing ? 'Done editing' : 'Edit shortcuts';
  e.currentTarget.setAttribute('aria-pressed',String(editing)); renderLinks();
});
document.getElementById('cancel-link').addEventListener('click', () => dialog.close());
document.getElementById('delete-link').addEventListener('click', () => saveLinks(links.filter((_,i) => i !== selected)));
document.getElementById('shortcut-form').addEventListener('submit', e => {
  e.preventDefault(); const name = nameInput.value.trim(); let raw = urlInput.value.trim();
  if (!/^[a-z][a-z\d+.-]*:/i.test(raw)) raw = 'https://' + raw;
  let url;
  try { url = new URL(raw); if (!['https:','http:'].includes(url.protocol) || !url.hostname || url.username || url.password) throw new Error(); }
  catch { error.textContent = 'Enter a valid HTTP or HTTPS website without login credentials.'; return; }
  if (!name) { error.textContent = 'Give this shortcut a name.'; return; }
  const next = [...links], link = {Name:name, Url:url.href};
  if (selected < 0) next.push(link); else next[selected] = link;
  saveLinks(next);
});
const tips = [ ['A lighter web. A clearer mind.','Keep work, play and research in their own workspaces.'], ['Your next action is a shortcut away.','Press Ctrl + K to search tabs, bookmarks, history and browser commands.'], ['Give your game some breathing room.','Gaming Mode unloads background tabs when possible. Your active tab stays ready.'], ['A little more privacy, built in.','Tracker Shield blocks matching requests. Manage exceptions in Settings.'] ];
let tip = 0;
function changeTip(delta) { tip = (tip + delta + tips.length) % tips.length; document.getElementById('tip-title').textContent = tips[tip][0]; document.getElementById('tip-copy').textContent = tips[tip][1]; }
document.getElementById('previous-tip').addEventListener('click', () => changeTip(-1));
document.getElementById('next-tip').addEventListener('click', () => changeTip(1));
renderLinks(); send('home-ready');
