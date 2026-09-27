const featherToken=__feather_token_json__;
function send(action,extra){window.chrome.webview.postMessage(Object.assign({action:action},extra||{},{__featherToken:featherToken}));}
function preset(name){send('welcome-preset',{preset:name});document.getElementById('status').textContent=name==='low'?'Low Memory profile selected.':'Balanced profile selected.';}