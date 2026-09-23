const {chromium}=require('playwright');
const assert=require('node:assert/strict');
(async()=>{
 const browser=await chromium.launch({headless:true});
 const page=await browser.newPage({viewport:{width:1320,height:820}});const errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.addInitScript(()=>{ window.sent=[];window.listeners=[];window.chrome={webview:{postMessage:m=>window.sent.push(m),addEventListener:(n,cb)=>window.listeners.push(cb)}}; });
 await page.goto('file://'+process.cwd()+'/qa/start.html');
 await page.evaluate(()=>listeners.forEach(f=>f({data:{type:'home-stats',memory:'612 MB',memoryLabel:'Private RAM',cpu:'2.5% CPU',ratio:.87,loaded:3,cold:1,sleeping:0,blocked:245,memorySaver:true,shield:true,gaming:false}})));
 assert.equal(await page.locator('.shortcut').count(),6);assert.equal(await page.locator('#memory').textContent(),'612 MB');assert.equal(await page.locator('#memory-label').textContent(),'Private RAM');assert.equal(await page.locator('#cpu').textContent(),'2.5% CPU');
 await page.screenshot({path:'qa/home-desktop.png',fullPage:true});
 await page.locator('.add-link').click(); await page.locator('#shortcut-name').fill('Example');await page.locator('#shortcut-url').fill('javascript:alert(1)');await page.locator('#shortcut-form button[type=submit]').click();assert.match(await page.locator('#shortcut-error').textContent(),/valid HTTP/);
 await page.locator('#shortcut-url').fill('example.com');await page.locator('#shortcut-form button[type=submit]').click();
 let saved=await page.evaluate(()=>sent.find(m=>m.action==='save-home-links'));assert.equal(saved.links.length,7);assert.equal(saved.links[6].Url,'https://example.com/');
 await page.evaluate(links=>listeners.forEach(f=>f({data:{type:'home-links',links}})),saved.links);assert.equal(await page.locator('.shortcut').count(),7);
 await page.locator('#edit-links').click();await page.locator('.shortcut').last().click();await page.locator('#shortcut-name').fill('Changed');await page.locator('#shortcut-form button[type=submit]').click();
 saved=await page.evaluate(()=>sent.filter(m=>m.action==='save-home-links').at(-1));assert.equal(saved.links[6].Name,'Changed');
 await page.evaluate(links=>listeners.forEach(f=>f({data:{type:'home-links',links}})),saved.links);
 await page.locator('.shortcut').last().click();await page.locator('#delete-link').click();saved=await page.evaluate(()=>sent.filter(m=>m.action==='save-home-links').at(-1));assert.equal(saved.links.length,6);
 await page.locator('#q').fill('test search');await page.locator('#q').press('Enter');assert.ok(await page.evaluate(()=>sent.some(m=>m.action==='home-navigate'&&m.url==='test search')));
 await page.locator('#game-toggle').click();assert.ok(await page.evaluate(()=>sent.some(m=>m.action==='toggle-game')));
 await page.locator('#next-tip').click();assert.match(await page.locator('#tip-title').textContent(),/shortcut/);
 for(const width of [900,650,390]){await page.setViewportSize({width,height:820});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),`No horizontal overflow at ${width}`);await page.screenshot({path:`qa/home-${width}.png`,fullPage:true});}
 for(const name of ['settings','library','welcome']){await page.setViewportSize({width:1100,height:820});await page.goto('file://'+process.cwd()+`/qa/${name}.html`);await page.screenshot({path:`qa/${name}.png`,fullPage:true});if(name==='settings'){await page.locator('[data-profile=balanced]').click();assert.ok(await page.evaluate(()=>sent.some(m=>m.action==='apply-webview3-profile'&&m.profile==='balanced')));}}

 await page.route('https://autofill.test/**', route => route.fulfill({contentType:'text/html',body:'<input type="email"><input type="password">'}));
 await page.goto('https://autofill.test/login');
 const fs=require('node:fs');
 const scriptPath=fs.existsSync('src/FeatherBrowser/Assets/Scripts/autofill.js')?'src/FeatherBrowser/Assets/Scripts/autofill.js':'project/src/FeatherBrowser/Assets/Scripts/autofill.js';
 const template=fs.readFileSync(scriptPath,'utf8');
 const fillScript=origin=>template.replace('__ORIGIN_JSON__',JSON.stringify(origin)).replace('__USERNAME_JSON__',JSON.stringify('test@example.test')).replace('__PASSWORD_JSON__',JSON.stringify('synthetic-test-value'));
 assert.equal(await page.evaluate(fillScript('https://another.test')),'origin-changed');
 assert.equal(await page.locator('input[type=password]').inputValue(),'');
 assert.equal(await page.evaluate(fillScript('https://autofill.test')),'filled');
 assert.equal(await page.locator('input[type=password]').inputValue(),'synthetic-test-value');
 assert.deepEqual(errors,[]);await browser.close();console.log('PASS: shortcut add/edit/delete, unsafe URL rejection, search/toggle messages, live stats, tips, 3 responsive sizes and four pages without JS errors.');
})().catch(e=>{console.error(e);process.exit(1)});
