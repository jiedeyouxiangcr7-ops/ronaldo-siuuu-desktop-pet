// Browser QA using local Chrome's DevTools protocol. No npm packages required.
const {spawn}=require('node:child_process'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'..'),profile=path.join(__dirname,'qa-profile');
const pause=ms=>new Promise(r=>setTimeout(r,ms));
const chrome=spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',['--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check','--remote-debugging-port=0','--user-data-dir='+profile,'--window-size=1100,800','about:blank'],{windowsHide:true});
let chromeErrors='';
const endpoint=new Promise((resolve,reject)=>{chrome.stderr.on('data',b=>{chromeErrors+=b;const match=chromeErrors.match(/DevTools listening on (ws:\/\/[^\s]+)/);if(match)resolve(match[1])});chrome.on('exit',code=>reject(new Error('Chrome exited '+code)));setTimeout(()=>reject(new Error('Chrome startup timed out')),20000).unref()});
(async()=>{let ws;try{
ws=new WebSocket(await endpoint);await new Promise((r,j)=>{ws.onopen=r;ws.onerror=j});
let next=0;const pending=new Map();ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id&&pending.has(m.id)){const {r,j}=pending.get(m.id);pending.delete(m.id);m.error?j(new Error(JSON.stringify(m.error))):r(m.result)}};
function call(method,params={},sessionId){return new Promise((r,j)=>{const id=++next;pending.set(id,{r,j});ws.send(JSON.stringify({id,method,params,sessionId}))})}
const targets=await call('Target.getTargets'),target=targets.targetInfos.find(x=>x.type==='page');
const {sessionId}=await call('Target.attachToTarget',{targetId:target.targetId,flatten:true});
const c=(m,p)=>call(m,p,sessionId);
await c('Page.enable');await c('Runtime.enable');
await c('Page.navigate',{url:require('node:url').pathToFileURL(path.join(root,'效果预览.html')).href});
async function evaluate(expression){const r=await c('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:true});if(r.exceptionDetails)throw new Error(JSON.stringify(r.exceptionDetails));return r.result.value}
let result;for(let i=0;i<100;i++){await pause(100);result=await evaluate('typeof ready!=="undefined" && ready');if(result)break}if(!result)throw new Error('Sprites failed to load');
const details=await evaluate('({sprites:sprites.length,audioReady:audio.readyState,audioDuration:audio.duration,overflow:document.documentElement.scrollWidth>innerWidth})');
if(details.sprites!==8||details.audioReady<1||details.overflow)throw new Error('Invalid page state '+JSON.stringify(details));
await evaluate('celebrate();celebrate();({running,disabled:button.disabled})');
await pause(1780);const celebration=await evaluate('({running,audioTime:audio.currentTime,audioPaused:audio.paused,status:status.textContent})');
if(!celebration.running||celebration.audioTime<=0)throw new Error('Celebration/audio did not run '+JSON.stringify(celebration));
const shot=await c('Page.captureScreenshot',{format:'png'});fs.writeFileSync(path.join(root,'Preview','互动预览.png'),Buffer.from(shot.data,'base64'));
await pause(2200);const idle=await evaluate('({running,disabled:button.disabled,status:status.textContent})');if(idle.running||idle.disabled)throw new Error('Did not return to idle');
await evaluate('document.getElementById("mute").click()');if(!(await evaluate('audio.muted')))throw new Error('Mute failed');
await c('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});await pause(150);const mobileOverflow=await evaluate('document.documentElement.scrollWidth>innerWidth');
const report={result:'PASS',details,celebration,idle,mute:true,mobileOverflow};
fs.writeFileSync(path.join(root,'Source','preview-test-result.json'),JSON.stringify(report,null,2));console.log(JSON.stringify(report));
if(mobileOverflow)throw new Error('Mobile overflow');
await call('Browser.close');ws.close();
}catch(e){console.error(e);if(ws)ws.close();chrome.kill();process.exitCode=1}})();
