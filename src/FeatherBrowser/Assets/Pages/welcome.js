const featherToken=__FEATHER_TOKEN_JSON__;
function send(action,extra){window.chrome.webview.postMessage(Object.assign({action:action,__featherToken:featherToken},extra||{}));}
function preset(name){send('welcome-preset',{preset:name});document.getElementById('status').textContent=name==='low'?'Low Memory profile selected.':'Balanced profile selected.';}