const initial=__SETTINGS_JSON__;
const $=id=>document.getElementById(id);
const bool=(id,key)=>{const el=$(id);if(el)el.checked=!!initial[key];};

const fieldKeys={
  search:'SearchEngine',
  startup:'StartupMode',
  crashSave:'CrashRecoveryAutosave',
  game:'GameMode',
  muteBg:'MuteBackgroundTabs',
  keepAudio:'KeepAudioTabsLoaded',
  hibernateMin:'HibernateWhenMinimized',
  eco:'EcoMode',
  lowMemory:'LowMemoryMode',
  adaptiveMemory:'AdaptiveMemoryMode',
  coldWorkspaces:'ColdUnloadOtherWorkspaces',
  shield:'ShieldEnabled',
  strict:'StrictBlocking',
  thirdParty:'BlockThirdPartyTrackers',
  cosmetic:'CosmeticBlocking',
  stripTracking:'StripTrackingParameters',
  blockNotifications:'BlockNotificationPrompts',
  doNotTrack:'SendDoNotTrack',
  compact:'CompactUi',
  statusbar:'ShowStatusBar',
  workspaceSidebar:'ShowWorkspaceSidebar',
  autoMemory:'AutoMemoryGuard',
  dataBackups:'DataBackupsEnabled'
};

function loadInitial(){
  $('search').value=initial.SearchEngine||'Google';
  $('startup').value=initial.StartupMode||'Restore';

  Object.entries(fieldKeys).forEach(([id,key])=>{
    const el=$(id);
    if(el && el.type==='checkbox') el.checked=!!initial[key];
  });

  $('sleep').value=initial.SleepAfterSeconds||2;
  $('grace').value=initial.BackgroundGraceSeconds??10;
  $('maxLoaded').value=initial.MaxLoadedTabs||1;
  $('unload').value=initial.UnloadAfterSeconds||5;
  $('memoryLimit').value=initial.MemoryGuardMb||700;

  if($('keepAlive') && initial.KeepAliveSites!=null) $('keepAlive').value=initial.KeepAliveSites;
  if($('allowlist') && initial.AllowlistedSites!=null) $('allowlist').value=initial.AllowlistedSites;
  if($('rules') && initial.CustomBlockRules!=null) $('rules').value=initial.CustomBlockRules;

  syncMirrors();
  updateStats();
  setDirty(false);
}

function post(action,extra={}){
  window.chrome.webview.postMessage({action,...extra});
}

function setSection(id,resetSearch=true){
  document.querySelectorAll('nav button').forEach(x=>x.classList.toggle('active',x.dataset.section===id));
  document.querySelectorAll('.section').forEach(x=>x.classList.toggle('active',x.id===id));

  const section=$(id);
  if(section){
    $('pageTitle').textContent=section.dataset.title||id;
    $('pageSubtitle').textContent=section.dataset.subtitle||'';
  }

  if(resetSearch){
    $('settingsSearch').value='';
    clearSearch();
  }
}

document.querySelectorAll('nav button[data-section]').forEach(btn=>{
  btn.addEventListener('click',()=>setSection(btn.dataset.section,true));
});

document.querySelectorAll('[data-action]').forEach(btn=>{
  btn.addEventListener('click',()=>post(btn.dataset.action));
});

function syncMirrors(){
  if($('gameMirror')) $('gameMirror').checked=$('game').checked;
}

$('game').addEventListener('change',()=>{
  $('gameMirror').checked=$('game').checked;
  setDirty(true);
});
$('gameMirror').addEventListener('change',()=>{
  $('game').checked=$('gameMirror').checked;
  setDirty(true);
});

function updateStats(){
  $('statRam').textContent=`${Number($('memoryLimit').value||700)} MB`;
  $('statTabs').textContent=String(Number($('maxLoaded').value||1));
  $('statUnload').textContent=`${Number($('unload').value||5)}s`;
}

['memoryLimit','maxLoaded','unload'].forEach(id=>{
  $(id).addEventListener('input',()=>{
    updateStats();
    setDirty(true);
  });
});

function setDirty(dirty){
  $('saveTitle').textContent=dirty?'Unsaved changes':'Ready to apply';
  $('saveSubtitle').textContent=dirty?'Review your changes, then press Apply.':'Changes stay local until you press Apply.';
}

document.querySelectorAll('input,select,textarea').forEach(el=>{
  if(['settingsSearch','game','gameMirror','memoryLimit','maxLoaded','unload'].includes(el.id)) return;
  el.addEventListener('input',()=>setDirty(true));
  el.addEventListener('change',()=>setDirty(true));
});

$('resetPerformance').addEventListener('click',()=>{
  $('maxLoaded').value=1;
  $('unload').value=5;
  $('sleep').value=2;
  $('memoryLimit').value=700;
  $('lowMemory').checked=true;
  $('adaptiveMemory').checked=true;
  $('autoMemory').checked=true;
  updateStats();
  setDirty(true);
});

$('discard').addEventListener('click',()=>loadInitial());

$('apply').addEventListener('click',()=>{
  post('save-settings',{settings:{
    SearchEngine:$('search').value,
    StartupMode:$('startup').value,
    CrashRecoveryAutosave:$('crashSave').checked,
    GameMode:$('game').checked,
    MuteBackgroundTabs:$('muteBg').checked,
    KeepAudioTabsLoaded:$('keepAudio').checked,
    HibernateWhenMinimized:$('hibernateMin').checked,
    EcoMode:$('eco').checked,
    LowMemoryMode:$('lowMemory').checked,
    MaxLoadedTabs:Number($('maxLoaded').value||1),
    UnloadAfterSeconds:Number($('unload').value||5),
    SleepAfterSeconds:Number($('sleep').value||2),
    BackgroundGraceSeconds:Number($('grace').value||0),
    ShieldEnabled:$('shield').checked,
    StrictBlocking:$('strict').checked,
    BlockThirdPartyTrackers:$('thirdParty').checked,
    CosmeticBlocking:$('cosmetic').checked,
    StripTrackingParameters:$('stripTracking').checked,
    BlockNotificationPrompts:$('blockNotifications').checked,
    SendDoNotTrack:$('doNotTrack').checked,
    CompactUi:$('compact').checked,
    ShowStatusBar:$('statusbar').checked,
    ShowWorkspaceSidebar:$('workspaceSidebar').checked,
    AdaptiveMemoryMode:$('adaptiveMemory').checked,
    ColdUnloadOtherWorkspaces:$('coldWorkspaces').checked,
    AutoMemoryGuard:$('autoMemory').checked,
    MemoryGuardMb:Number($('memoryLimit').value||700),
    DataBackupsEnabled:$('dataBackups').checked,
    KeepAliveSites:$('keepAlive').value,
    AllowlistedSites:$('allowlist').value,
    CustomBlockRules:$('rules').value
  }});

  const s=$('saved');
  s.classList.add('show');
  $('saveTitle').textContent='Settings saved';
  $('saveSubtitle').textContent='All changes have been applied.';
  setTimeout(()=>s.classList.remove('show'),1300);
});

function clearSearch(){
  $('searchEmpty').classList.remove('show');
  document.querySelectorAll('.section .row,.section .textblock,.section .card,.section .statgrid,.section .hero-callout').forEach(el=>el.style.display='');
}

$('settingsSearch').addEventListener('input',e=>{
  const q=e.target.value.trim().toLowerCase();
  if(!q){clearSearch();return;}

  const sections=[...document.querySelectorAll('.section')];
  const matchSection=sections.find(section=>section.textContent.toLowerCase().includes(q));

  if(!matchSection){
    clearSearch();
    $('searchEmpty').classList.add('show');
    return;
  }

  setSection(matchSection.id,false);
  clearSearch();

  let matched=false;
  const active=matchSection;
  active.querySelectorAll('.row,.textblock').forEach(row=>{
    const hit=row.textContent.toLowerCase().includes(q);
    row.style.display=hit?'':'none';
    if(hit) matched=true;
  });

  active.querySelectorAll('.card').forEach(card=>{
    const candidates=[...card.querySelectorAll('.row,.textblock')];
    if(candidates.length){
      card.style.display=candidates.some(x=>x.style.display!=='none')?'':'none';
    }
  });

  active.querySelectorAll('.hero-callout,.statgrid').forEach(el=>el.style.display='none');
  $('searchEmpty').classList.toggle('show',!matched);
});

loadInitial();
updateStats();

document.querySelectorAll('[data-profile]').forEach(button => button.addEventListener('click', () => post('apply-webview3-profile', {profile:button.dataset.profile})));
