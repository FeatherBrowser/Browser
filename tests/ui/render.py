from pathlib import Path
import json,re,xml.etree.ElementTree as ET
r=Path('src/FeatherBrowser/Assets/Pages');out=Path('qa');out.mkdir(exist_ok=True)
links=[{'Name':n,'Url':u} for n,u in [('YouTube','https://www.youtube.com/'),('Twitch','https://www.twitch.tv/'),('Reddit','https://www.reddit.com/'),('Spotify','https://open.spotify.com/'),('Notion','https://www.notion.so/'),('GitHub','https://github.com/')]]
settings={'SearchEngine':'Google','StartupMode':'Restore','EcoMode':True,'LowMemoryMode':True,'ShieldEnabled':True,'MemoryGuardMb':700,'MaxLoadedTabs':3,'SleepAfterSeconds':5,'UnloadAfterSeconds':30}
values={'__ACCOUNT_JSON__':'{"IsPrivateMode":false,"IsSignedIn":false,"Email":"","UserId":"","AssuranceLevel":"aal1","HasVerifiedMfa":false,"HasLocalSyncKey":false,"RemoteSyncExists":false,"SyncEnabled":false,"CanApproveDevices":false,"TotpSetupActive":false,"TotpSecret":"","TotpQrCodeSvg":"","PairingCode":"","SyncStatus":"Not signed in","LastSync":"Never","Message":"","Devices":[]}','__SETTINGS_SECTION_JSON__':json.dumps('performance'),'__feather_token_json__':json.dumps('qa-token'),'__APP_VERSION__':'3.0.0','__SEARCH_ENGINE__':'Google','__SEARCH_PREFIX_JSON__':json.dumps('https://www.google.com/search?q='),'__QUICK_LINKS_JSON__':json.dumps(links),'__HOME_STATE_JSON__':json.dumps({'memorySaver':True,'shield':True,'gaming':False}),'__WORKSPACE_TEXT__':'Personal · 4 spaces','__SETTINGS_JSON__':json.dumps(settings),'__HISTORY_JSON__':'[]','__DOWNLOADS_JSON__':'[]','__BOOKMARKS_JSON__':'[]','__SECTION_JSON__':'"history"'}
for page in ['start','settings','library','welcome']:
 s=(r/f'{page}.html').read_text().replace('__PAGE_STYLES__',(r/'theme.css').read_text()+'\n'+(r/f'{page}.css').read_text()).replace('__PAGE_SCRIPT__',(r/f'{page}.js').read_text())
 for k,v in values.items():s=s.replace(k,v)
 s=re.sub(r'__[A-Z_]+__','',s);(out/f'{page}.html').write_text(s)
for p in Path('.').rglob('*.xaml'):ET.parse(p)
print('All XAML files are well-formed XML; composed four internal pages.')
