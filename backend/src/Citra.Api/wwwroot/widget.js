(function(){function e(e,n){e.replaceChildren();let r=null,i=null;for(let a of n.replace(/\r/g,``).split(`
`)){let n=a.trim();if(!n){r=i=null;continue}let o=/^(?:([-*+])|\d+[.)])\s+(.*)$/.exec(n);if(o){let n=o[1]?`UL`:`OL`;i?.tagName!==n&&(i=document.createElement(n),e.append(i)),r=null;let a=document.createElement(`li`);t(a,o[2]),i.append(a);continue}i=null,r?r.append(document.createElement(`br`)):(r=document.createElement(`p`),e.append(r));let s=/^#{1,6}\s+(.*)$/.exec(n);if(s){let e=document.createElement(`strong`);t(e,s[1]),r.append(e)}else t(r,n)}}function t(e,t){for(let n of t.split(/(\*\*[^*]+\*\*|`[^`]+`|\*[^*\s][^*]*\*)/)){let t=n.length>4&&n.startsWith(`**`)&&n.endsWith(`**`)?`strong`:n.length>2&&n.startsWith("`")&&n.endsWith("`")?`code`:n.length>2&&n.startsWith(`*`)&&n.endsWith(`*`)?`em`:null;if(!t){n&&e.append(n);continue}let r=document.createElement(t);r.textContent=n.slice(t===`strong`?2:1,t===`strong`?-2:-1),e.append(r)}}var n=document.currentScript,r=n?.dataset.bot,i=n?new URL(n.src).origin:``;async function a(){let e=await fetch(`${i}/api/public/bots/${r}`);return e.ok?e.json():null}var o=`
  :host { all: initial; font: 14px/1.5 system-ui, sans-serif; color: #1f2937; }
  * { box-sizing: border-box; font: inherit; }
  .bubble { position: fixed; right: 20px; bottom: 20px; z-index: 2147483647; width: 56px; height: 56px;
    border: 0; border-radius: 50%; background: var(--color); color: #fff; cursor: pointer;
    box-shadow: 0 4px 12px rgb(0 0 0 / 25%); display: grid; place-items: center; }
  .bubble svg { width: 26px; height: 26px; fill: currentColor; }
  .window { position: fixed; right: 20px; bottom: 88px; z-index: 2147483647; width: 360px; height: 520px;
    max-height: calc(100vh - 108px); background: #fff; border-radius: 12px; box-shadow: 0 8px 24px rgb(0 0 0 / 20%);
    display: flex; flex-direction: column; overflow: hidden; }
  .window[hidden] { display: none; }
  header { background: var(--color); color: #fff; padding: 12px 16px; display: flex; align-items: center; }
  header h2 { margin: 0; flex: 1; font-weight: 600; }
  header button { border: 0; background: none; color: inherit; cursor: pointer; font-size: 20px; line-height: 1; }
  .messages { flex: 1; overflow-y: auto; padding: 16px; display: flex; flex-direction: column; gap: 8px; }
  .message { max-width: 85%; padding: 8px 12px; border-radius: 12px; background: #f3f4f6; white-space: pre-wrap; }
  .message.user { align-self: flex-end; background: var(--color); color: #fff; }
  .message.answer { white-space: normal; }
  .message p { margin: 0; }
  .message p + p, .message p + ul, .message p + ol, .message ul + p, .message ol + p { margin-top: 6px; }
  .message ul, .message ol { margin: 4px 0 0; padding-left: 20px; }
  .message strong { font-weight: 600; }
  .message em { font-style: italic; }
  .message code { font-family: ui-monospace, monospace; font-size: 13px; background: rgb(0 0 0 / 6%);
    padding: 0 3px; border-radius: 4px; }
  .starters { display: flex; flex-wrap: wrap; gap: 6px; }
  .starters button { border: 1px solid var(--color); border-radius: 16px; padding: 4px 10px; background: #fff;
    color: var(--color); cursor: pointer; }
  details { margin-top: 6px; font-size: 12px; white-space: normal; }
  summary { cursor: pointer; color: var(--color); }
  /* Phones: the open chat fills the screen; the close button in the header closes it. */
  @media (max-width: 480px) {
    .window { inset: 0; width: auto; height: auto; max-height: none; border-radius: 0; }
    .window:not([hidden]) + .bubble { display: none; }
    input { font-size: 16px; } /* smaller text makes iPhones zoom in */
  }
  form { display: flex; gap: 8px; margin: 0; padding: 12px; border-top: 1px solid #e5e7eb; }
  input { flex: 1; padding: 8px 12px; border: 1px solid #d1d5db; border-radius: 8px; }
  form button { border: 0; border-radius: 8px; padding: 8px 14px; background: var(--color); color: #fff; cursor: pointer; }
`,s=`<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 3h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H8l-4 4V5a2 2 0 0 1 2-2z"/></svg>`;function c(e){let t=document.createElement(`div`);t.style.setProperty(`--color`,e.widgetColor);let n=t.attachShadow({mode:`open`});n.innerHTML=`<style>${o}</style>
    <section class="window" role="dialog" hidden>
      <header><h2></h2><button type="button" class="close" aria-label="Close chat">&times;</button></header>
      <div class="messages" aria-live="polite"></div>
      <form><input aria-label="Your question" placeholder="Ask a question..." /><button>Send</button></form>
    </section>
    <button class="bubble" type="button" aria-expanded="false">${s}</button>`;let r=n.querySelector(`.window`),i=n.querySelector(`.bubble`),a=n.querySelector(`input`);n.querySelector(`h2`).textContent=e.name,r.setAttribute(`aria-label`,e.name),i.setAttribute(`aria-label`,`Chat with ${e.name}`),l(n,e.welcomeMessage);for(let e of d.messages)l(n,e.text,e.sources,!!e.sources).classList.toggle(`user`,!e.sources);let c=document.createElement(`div`);c.className=`starters`;for(let t of e.starterQuestions){let e=document.createElement(`button`);e.type=`button`,e.textContent=t,e.addEventListener(`click`,()=>f(t)),c.append(e)}d.messages.length===0&&n.querySelector(`.messages`).append(c);let u=!1,f=async e=>{if(u||!e.trim())return;u=!0,c.remove(),a.value=``,l(n,e).classList.add(`user`);let t=l(n,`...`);try{await h(e,t)}catch{t.textContent=`Sorry, something went wrong. Please try again.`}u=!1},p=e=>{r.hidden=!e,i.setAttribute(`aria-expanded`,String(e)),(e?a:i).focus()};i.addEventListener(`click`,()=>p(r.hidden)),n.querySelector(`.close`).addEventListener(`click`,()=>p(!1)),n.querySelector(`form`).addEventListener(`submit`,e=>{e.preventDefault(),f(a.value)}),document.body.append(t)}function l(e,t,n=[],r=!1){let i=document.createElement(`div`);i.className=`message`,r?g(i,t):i.textContent=t,i.append(...n.map(_));let a=e.querySelector(`.messages`);return a.append(i),a.scrollTop=a.scrollHeight,i}var u=`citra-widget:${r}`,d=p()??{visitorId:f(),conversationId:null,messages:[]};function f(){if(typeof crypto.randomUUID==`function`)return crypto.randomUUID();let e=crypto.getRandomValues(new Uint8Array(16));return Array.from(e,e=>e.toString(16).padStart(2,`0`)).join(``)}function p(){try{return JSON.parse(localStorage.getItem(u)??`null`)}catch{return null}}function m(){try{localStorage.setItem(u,JSON.stringify(d))}catch{}}async function h(e,t){let n=await fetch(`${i}/api/public/bots/${r}/chat`,{method:`POST`,headers:{"Content-Type":`application/json`},body:JSON.stringify({visitorId:d.visitorId,conversationId:d.conversationId,question:e})});if(!n.ok||!n.body){n.status===404&&(d.conversationId=null),t.textContent=(await n.json().catch(()=>null))?.detail??`Sorry, something went wrong. Please try again.`;return}let a=[],o=``,s=n.body.pipeThrough(new TextDecoderStream).getReader(),c=``;for(;;){let{done:n,value:r}=await s.read();if(n)return;let i=(c+r).replace(/\r/g,``).split(`

`);c=i.pop();for(let n of i){let r=/^event: (.*)$/m.exec(n)?.[1],i=JSON.parse(/^data: (.*)$/m.exec(n)?.[1]??`null`);if(r===`start`)d.conversationId=i.conversationId;else if(r===`citations`)a=i.sources;else if(r===`token`)g(t,o+=i.text);else if(r===`done`){let n=a.filter(e=>i.citedSources.includes(e.number));g(t,i.content),t.append(...n.map(_)),d.messages.push({text:e},{text:i.content,sources:n}),m()}else r===`error`&&(t.textContent=i.message)}}}function g(t,n){t.classList.add(`answer`),e(t,n)}function _(e){let t=document.createElement(`details`),n=document.createElement(`summary`);return n.textContent=`[${e.number}] ${e.documentName}`+(e.pageNumber?`, page ${e.pageNumber}`:``),t.append(n,e.content),t}r?.includes(`/`)?a().then(e=>e?c(e):console.error(`Citra widget: bot "${r}" is not available here.`)).catch(()=>console.error(`Citra widget: could not reach the Citra API.`)):console.error(`Citra widget: add data-bot="company/bot" to the script tag.`)})();