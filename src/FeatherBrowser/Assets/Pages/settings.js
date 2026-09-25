const initial=__SETTINGS_JSON__;
const account=__ACCOUNT_JSON__;
const initialSection=__SETTINGS_SECTION_JSON__;
const $=id=>document.getElementById(id);

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
  dataBackups:'DataBackupsEnabled',
  autoSync:'AutoSyncEnabled',
  syncHistory:'SyncHistory',
  syncOpenTabs:'SyncOpenTabs'
};

function post(action,extra={}){
  window.chrome.webview.postMessage({action,...extra});
}

function text(id,value){
  const el=$(id);
  if(el) el.textContent=value??'';
}

function show(id,visible){
  const el=$(id);
  if(el) el.classList.toggle('hidden',!visible);
}

function loadInitial(){
  $('search').value=initial.SearchEngine||'Google';
  $('startup').value=initial.StartupMode||'Restore';

  Object.entries(fieldKeys).forEach(([id,key])=>{
    const el=$(id);
    if(el&&el.type==='checkbox') el.checked=!!initial[key];
  });

  $('sleep').value=initial.SleepAfterSeconds||2;
  $('grace').value=initial.BackgroundGraceSeconds??10;
  $('maxLoaded').value=initial.MaxLoadedTabs||1;
  $('unload').value=initial.UnloadAfterSeconds||5;
  $('memoryLimit').value=initial.MemoryGuardMb||700;
  if($('autoSyncMinutes')) $('autoSyncMinutes').value=initial.AutoSyncMinutes||5;

  if($('keepAlive')&&initial.KeepAliveSites!=null) $('keepAlive').value=initial.KeepAliveSites;
  if($('allowlist')&&initial.AllowlistedSites!=null) $('allowlist').value=initial.AllowlistedSites;
  if($('rules')&&initial.CustomBlockRules!=null) $('rules').value=initial.CustomBlockRules;

  syncMirrors();
  updateStats();
  setDirty(false);
}

function setSection(id,resetSearch=true,notifyHost=false){
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
  if(notifyHost){
    post('settings-section-changed',{section:id});
  }
}

document.querySelectorAll('nav button[data-section]').forEach(btn=>{
  btn.addEventListener('click',()=>setSection(btn.dataset.section,true,true));
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
    AutoSyncEnabled:$('autoSync').checked,
    AutoSyncMinutes:Number($('autoSyncMinutes').value||5),
    SyncHistory:$('syncHistory').checked,
    SyncOpenTabs:$('syncOpenTabs').checked,
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

function addAccountButton(container,label,action,primary=false,danger=false,extra={}){
  const button=document.createElement('button');
  button.className=`action${primary?' primary':''}${danger?' danger':''}`;
  button.textContent=label;
  button.addEventListener('click',()=>post(action,extra));
  container.appendChild(button);
}

function renderAccount(){
  const privateMode=!!account.IsPrivateMode;
  const signedIn=!!account.IsSignedIn;
  const aal2=(account.AssuranceLevel||'aal1').toLowerCase()==='aal2';
  const mfa=!!account.HasVerifiedMfa;
  const localKey=!!account.HasLocalSyncKey;
  const remote=!!account.RemoteSyncExists;

  text('accountHeroTag',privateMode?'Disabled in Private':remote?'Encrypted sync':'Local-only');
  text('accountStatusBadge',privateMode?'Private window':signedIn?'Signed in':'Signed out');
  text('accountStatusText',privateMode?'Account and sync controls are disabled in Private windows.':signedIn?'Feather account connected.':'Sign in or create an account to use encrypted sync.');
  show('accountSignedOutActions',!privateMode&&!signedIn);
  show('accountSignedInActions',!privateMode&&signedIn);
  show('accountIdentityRow',signedIn&&!privateMode);
  text('accountIdentity',account.Email||account.UserId||'Signed in');
  text('accountAal',(account.AssuranceLevel||'aal1').toUpperCase());

  text('mfaStatus',privateMode?'Unavailable in Private windows.':!signedIn?'Sign in to configure 2FA.':mfa?(aal2?'Authenticator verified for this session.':'Authenticator configured. Verify it to unlock sensitive sync operations.'):'No verified authenticator is configured.');
  show('mfaSetupButton',signedIn&&!privateMode&&!mfa);
  show('mfaVerifyButton',signedIn&&!privateMode&&mfa&&!aal2);
  show('mfaEnrollment',signedIn&&!privateMode&&!!account.TotpSetupActive);
  if(account.TotpSetupActive){
    text('mfaSecret',account.TotpSecret||'');
    const qr=$('mfaQr');
    if(qr){
      if(account.TotpQrCodeSvg) qr.src=`data:image/svg+xml;charset=utf-8,${encodeURIComponent(account.TotpQrCodeSvg)}`;
      else qr.removeAttribute('src');
    }
  }

  text('syncStatusBadge',privateMode?'Off':localKey&&remote?'Active':remote?'Locked':signedIn?'Not set up':'Off');
  text('syncStatusText',privateMode?'Feather never syncs private-window state.':account.SyncStatus||(!signedIn?'Sign in to use Feather Sync.':'Encrypted sync is not configured.'));

  const actions=$('syncActions');
  if(actions){
    actions.replaceChildren();
    if(signedIn&&!privateMode&&aal2&&!remote) addAccountButton(actions,'Enable encrypted sync','sync-enable',true);
    if(signedIn&&!privateMode&&remote&&localKey) addAccountButton(actions,'Sync now','sync-now',true);
    if(signedIn&&!privateMode&&remote&&!localKey){
      addAccountButton(actions,'Request device approval','sync-request-approval',true);
      addAccountButton(actions,'Use recovery backup','sync-recover-file');
      addAccountButton(actions,'Enter recovery key','sync-recover');
      if(account.PairingCode) addAccountButton(actions,'Check approval','sync-check-approval');
    }
    if(signedIn&&!privateMode&&remote&&aal2) addAccountButton(actions,'Delete cloud sync','sync-delete',false,true);
  }

  show('pairingPanel',signedIn&&!privateMode&&!localKey&&!!account.PairingCode);
  text('pairingCode',account.PairingCode||'');

  const list=$('deviceList');
  if(list){
    list.replaceChildren();
    const devices=Array.isArray(account.Devices)?account.Devices:[];
    if(!signedIn||privateMode||devices.length===0){
      const empty=document.createElement('div');
      empty.className='device-empty';
      empty.textContent=privateMode?'Device management is disabled in Private windows.':signedIn?'No registered devices yet.':'Sign in and verify 2FA to view devices.';
      list.appendChild(empty);
    }else{
      devices.forEach(device=>{
        const item=document.createElement('div');
        item.className='device-item';
        const copy=document.createElement('div');
        const name=document.createElement('div');
        name.className='device-name';
        name.textContent=`${device.Name||'Feather device'}${device.IsCurrent?' · This device':''}`;
        const meta=document.createElement('div');
        meta.className='device-meta';
        meta.textContent=`${device.Status||'unknown'}${device.PairingCode?` · ${device.PairingCode}`:''}`;
        copy.append(name,meta);
        item.appendChild(copy);
        if(device.Status==='pending'&&account.CanApproveDevices&&!device.IsCurrent){
          const deviceActions=document.createElement('div');
          deviceActions.className='actions';
          addAccountButton(deviceActions,'Approve','device-approve',true,false,{deviceId:device.Id});
          addAccountButton(deviceActions,'Deny','device-deny',false,true,{deviceId:device.Id});
          item.appendChild(deviceActions);
        }
        list.appendChild(item);
      });
    }
  }

  const message=$('accountMessage');
  if(message){
    message.textContent=account.Message||'';
    message.classList.toggle('show',!!account.Message);
  }
}

function clearSearch(){
  $('searchEmpty').classList.remove('show');
  document.querySelectorAll('.section .row,.section .textblock,.section .card,.section .statgrid,.section .hero-callout,.section .device-item').forEach(el=>el.style.display='');
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
  setSection(matchSection.id,false,false);
  clearSearch();
  let matched=false;
  matchSection.querySelectorAll('.row,.textblock,.device-item').forEach(row=>{
    const hit=row.textContent.toLowerCase().includes(q);
    row.style.display=hit?'':'none';
    if(hit) matched=true;
  });
  matchSection.querySelectorAll('.card').forEach(card=>{
    const candidates=[...card.querySelectorAll('.row,.textblock,.device-item')];
    if(candidates.length) card.style.display=candidates.some(x=>x.style.display!=='none')?'':'none';
  });
  matchSection.querySelectorAll('.hero-callout,.statgrid').forEach(el=>el.style.display='none');
  $('searchEmpty').classList.toggle('show',!matched);
});

loadInitial();
updateStats();
renderAccount();

document.querySelectorAll('[data-profile]').forEach(button=>button.addEventListener('click',()=>post('apply-webview3-profile',{profile:button.dataset.profile})));
