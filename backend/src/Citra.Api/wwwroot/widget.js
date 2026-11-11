(function(){var e=document.currentScript,t=e?.dataset.bot,n=e?new URL(e.src).origin:``;async function r(){let e=await fetch(`${n}/api/public/bots/${t}`);return e.ok?e.json():null}var i=`
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
`,a=`<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 3h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H8l-4 4V5a2 2 0 0 1 2-2z"/></svg>`;function o(e){let t=document.createElement(`div`);t.style.setProperty(`--color`,e.widgetColor);let n=t.attachShadow({mode:`open`});n.innerHTML=`<style>${i}</style>
    <section class="window" role="dialog" hidden>
      <header><h2></h2><button type="button" class="close" aria-label="Close chat">&times;</button></header>
      <div class="messages" aria-live="polite"></div>
      <form><input aria-label="Your question" placeholder="Ask a question..." /><button>Send</button></form>
    </section>
    <button class="bubble" type="button" aria-expanded="false">${a}</button>`;let r=n.querySelector(`.window`),o=n.querySelector(`.bubble`),c=n.querySelector(`input`);n.querySelector(`h2`).textContent=e.name,r.setAttribute(`aria-label`,e.name),o.setAttribute(`aria-label`,`Chat with ${e.name}`),s(n,e.welcomeMessage);for(let e of l.messages)s(n,e.text,e.sources).classList.toggle(`user`,!e.sources);let u=document.createElement(`div`);u.className=`starters`;for(let t of e.starterQuestions){let e=document.createElement(`button`);e.type=`button`,e.textContent=t,e.addEventListener(`click`,()=>f(t)),u.append(e)}l.messages.length===0&&n.querySelector(`.messages`).append(u);let d=!1,f=async e=>{if(d||!e.trim())return;d=!0,u.remove(),c.value=``,s(n,e).classList.add(`user`);let t=s(n,`...`);try{await p(e,t)}catch{t.textContent=`Sorry, something went wrong. Please try again.`}d=!1},m=e=>{r.hidden=!e,o.setAttribute(`aria-expanded`,String(e)),(e?c:o).focus()};o.addEventListener(`click`,()=>m(r.hidden)),n.querySelector(`.close`).addEventListener(`click`,()=>m(!1)),n.querySelector(`form`).addEventListener(`submit`,e=>{e.preventDefault(),f(c.value)}),document.body.append(t)}function s(e,t,n=[]){let r=document.createElement(`div`);r.className=`message`,r.textContent=t,r.append(...n.map(m));let i=e.querySelector(`.messages`);return i.append(r),i.scrollTop=i.scrollHeight,r}var c=`citra-widget:${t}`,l=d()??{visitorId:u(),conversationId:null,messages:[]};function u(){if(typeof crypto.randomUUID==`function`)return crypto.randomUUID();let e=crypto.getRandomValues(new Uint8Array(16));return Array.from(e,e=>e.toString(16).padStart(2,`0`)).join(``)}function d(){try{return JSON.parse(localStorage.getItem(c)??`null`)}catch{return null}}function f(){try{localStorage.setItem(c,JSON.stringify(l))}catch{}}async function p(e,r){let i=await fetch(`${n}/api/public/bots/${t}/chat`,{method:`POST`,headers:{"Content-Type":`application/json`},body:JSON.stringify({visitorId:l.visitorId,conversationId:l.conversationId,question:e})});if(!i.ok||!i.body){i.status===404&&(l.conversationId=null),r.textContent=(await i.json().catch(()=>null))?.detail??`Sorry, something went wrong. Please try again.`;return}let a=[],o=``,s=i.body.pipeThrough(new TextDecoderStream).getReader(),c=``;for(;;){let{done:t,value:n}=await s.read();if(t)return;let i=(c+n).replace(/\r/g,``).split(`

`);c=i.pop();for(let t of i){let n=/^event: (.*)$/m.exec(t)?.[1],i=JSON.parse(/^data: (.*)$/m.exec(t)?.[1]??`null`);if(n===`start`)l.conversationId=i.conversationId;else if(n===`citations`)a=i.sources;else if(n===`token`)r.textContent=o+=i.text;else if(n===`done`){let t=a.filter(e=>i.citedSources.includes(e.number));r.textContent=i.content,r.append(...t.map(m)),l.messages.push({text:e},{text:i.content,sources:t}),f()}else n===`error`&&(r.textContent=i.message)}}}function m(e){let t=document.createElement(`details`),n=document.createElement(`summary`);return n.textContent=`[${e.number}] ${e.documentName}`+(e.pageNumber?`, page ${e.pageNumber}`:``),t.append(n,e.content),t}t?.includes(`/`)?r().then(e=>e?o(e):console.error(`Citra widget: bot "${t}" is not available here.`)).catch(()=>console.error(`Citra widget: could not reach the Citra API.`)):console.error(`Citra widget: add data-bot="company/bot" to the script tag.`)})();