// Citra website widget: embedded with <script src="https://api.example.com/widget.js" data-bot="company/bot"></script>.

interface BotConfig {
  name: string;
  welcomeMessage: string;
  widgetColor: string;
  starterQuestions: string[];
}

/** A numbered passage the answer is based on (from the "citations" event). */
interface Source {
  number: number;
  documentName: string;
  pageNumber: number | null;
  content: string;
}

const script = document.currentScript as HTMLScriptElement | null;
const bot = script?.dataset['bot'];
// The API serves this script, so the API lives where the script came from.
const api = script ? new URL(script.src).origin : '';

async function loadBot(): Promise<BotConfig | null> {
  const response = await fetch(`${api}/api/public/bots/${bot}`);
  return response.ok ? response.json() : null;
}

// The widget lives in a Shadow DOM, so the website's CSS cannot change it and its CSS cannot leak out.
// `all: initial` also stops inherited styles (font, colour) from coming in.
const STYLE = `
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
`;
const CHAT_ICON =
  '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 3h16a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2H8l-4 4V5a2 2 0 0 1 2-2z"/></svg>';

function showWidget(config: BotConfig): void {
  const host = document.createElement('div');
  host.style.setProperty('--color', config.widgetColor);
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = `<style>${STYLE}</style>
    <section class="window" role="dialog" hidden>
      <header><h2></h2><button type="button" class="close" aria-label="Close chat">&times;</button></header>
      <div class="messages" aria-live="polite"></div>
      <form><input aria-label="Your question" placeholder="Ask a question..." /><button>Send</button></form>
    </section>
    <button class="bubble" type="button" aria-expanded="false">${CHAT_ICON}</button>`;

  const chatWindow = root.querySelector<HTMLElement>('.window')!;
  const bubble = root.querySelector<HTMLButtonElement>('.bubble')!;
  const input = root.querySelector('input')!;
  root.querySelector('h2')!.textContent = config.name;
  chatWindow.setAttribute('aria-label', config.name);
  bubble.setAttribute('aria-label', `Chat with ${config.name}`);
  addMessage(root, config.welcomeMessage);
  for (const message of chat.messages) {
    addMessage(root, message.text, message.sources).classList.toggle('user', !message.sources);
  }
  const starters = document.createElement('div');
  starters.className = 'starters';
  for (const question of config.starterQuestions) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = question;
    button.addEventListener('click', () => ask(question));
    starters.append(button);
  }
  if (chat.messages.length === 0) {
    root.querySelector('.messages')!.append(starters);
  }

  let busy = false;
  const ask = async (question: string) => {
    if (busy || !question.trim()) {
      return;
    }
    busy = true;
    starters.remove();
    input.value = '';
    addMessage(root, question).classList.add('user');
    const answer = addMessage(root, '...');
    try {
      await streamAnswer(question, answer);
    } catch {
      answer.textContent = 'Sorry, something went wrong. Please try again.';
    }
    busy = false;
  };

  const toggle = (open: boolean) => {
    chatWindow.hidden = !open;
    bubble.setAttribute('aria-expanded', String(open));
    (open ? input : bubble).focus();
  };
  bubble.addEventListener('click', () => toggle(chatWindow.hidden));
  root.querySelector('.close')!.addEventListener('click', () => toggle(false));
  root.querySelector('form')!.addEventListener('submit', (event) => {
    event.preventDefault();
    ask(input.value);
  });
  document.body.append(host);
}

function addMessage(root: ShadowRoot, text: string, sources: Source[] = []): HTMLElement {
  const message = document.createElement('div');
  message.className = 'message';
  message.textContent = text;
  message.append(...sources.map(sourceLink));
  const messages = root.querySelector('.messages')!;
  messages.append(message);
  messages.scrollTop = messages.scrollHeight;
  return message;
}

/** The visitor's chat with this bot, kept in localStorage so it continues after a page reload. */
interface SavedChat {
  visitorId: string;
  conversationId: string | null;
  /** Questions have no sources; answers have the sources they cite. */
  messages: { text: string; sources?: Source[] }[];
}

const STORAGE_KEY = `citra-widget:${bot}`;
const chat: SavedChat = readChat() ?? { visitorId: newVisitorId(), conversationId: null, messages: [] };

/** A random id. crypto.randomUUID exists only on https:// pages; getRandomValues works on http:// sites too. */
function newVisitorId(): string {
  if (typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (b) => b.toString(16).padStart(2, '0')).join('');
}

// localStorage can be blocked (private windows, strict settings); the chat then lasts until the page reloads.
function readChat(): SavedChat | null {
  try {
    return JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null');
  } catch {
    return null;
  }
}

function saveChat(): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(chat));
  } catch {
    // not saved
  }
}

/** Asks the bot and writes the answer into `answer` as it arrives, then lists the sources it cites. */
async function streamAnswer(question: string, answer: HTMLElement): Promise<void> {
  const response = await fetch(`${api}/api/public/bots/${bot}/chat`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ visitorId: chat.visitorId, conversationId: chat.conversationId, question }),
  });
  if (!response.ok || !response.body) {
    if (response.status === 404) {
      chat.conversationId = null; // the saved chat was deleted: the next question starts a new one
    }
    const problem = await response.json().catch(() => null);
    answer.textContent = problem?.detail ?? 'Sorry, something went wrong. Please try again.';
    return;
  }

  let sources: Source[] = [];
  let text = '';
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  let buffer = '';
  while (true) {
    const { done, value } = await reader.read();
    if (done) {
      return;
    }
    // Server-sent events: "event: type" and "data: json" lines, separated by a blank line.
    const events = (buffer + value).replace(/\r/g, '').split('\n\n');
    buffer = events.pop()!;
    for (const block of events) {
      const type = /^event: (.*)$/m.exec(block)?.[1];
      const data = JSON.parse(/^data: (.*)$/m.exec(block)?.[1] ?? 'null');
      if (type === 'start') {
        chat.conversationId = data.conversationId;
      } else if (type === 'citations') {
        sources = data.sources;
      } else if (type === 'token') {
        answer.textContent = text += data.text;
      } else if (type === 'done') {
        const cited = sources.filter((s) => data.citedSources.includes(s.number));
        answer.textContent = data.content;
        answer.append(...cited.map(sourceLink));
        chat.messages.push({ text: question }, { text: data.content, sources: cited });
        saveChat();
      } else if (type === 'error') {
        answer.textContent = data.message;
      }
    }
  }
}

/** A small "[1] Handbook.pdf, page 3" link that opens the cited passage. */
function sourceLink(source: Source): HTMLElement {
  const details = document.createElement('details');
  const summary = document.createElement('summary');
  summary.textContent = `[${source.number}] ${source.documentName}` + (source.pageNumber ? `, page ${source.pageNumber}` : '');
  details.append(summary, source.content);
  return details;
}

if (!bot?.includes('/')) {
  console.error('Citra widget: add data-bot="company/bot" to the script tag.');
} else {
  loadBot()
    .then((config) => (config ? showWidget(config) : console.error(`Citra widget: bot "${bot}" is not available here.`)))
    .catch(() => console.error('Citra widget: could not reach the Citra API.'));
}
