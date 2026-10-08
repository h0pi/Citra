import { ServerSentEvent, readServerSentEvents } from './server-sent-events';

/** A body that delivers these pieces one read at a time (strings are UTF-8 encoded). */
function body(...pieces: (string | Uint8Array)[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const piece of pieces) {
        controller.enqueue(typeof piece === 'string' ? encoder.encode(piece) : piece);
      }
      controller.close();
    },
  });
}

async function all(stream: ReadableStream<Uint8Array>): Promise<ServerSentEvent[]> {
  const events: ServerSentEvent[] = [];
  for await (const event of readServerSentEvents(stream)) {
    events.push(event);
  }
  return events;
}

describe('readServerSentEvents', () => {
  it('reads events with their type and data', async () => {
    const events = await all(
      body('event: start\ndata: {"a":1}\n\nevent: token\ndata: {"text":"Hi"}\n\n'),
    );

    expect(events).toEqual([
      { event: 'start', data: '{"a":1}' },
      { event: 'token', data: '{"text":"Hi"}' },
    ]);
  });

  it('puts together events split anywhere across reads', async () => {
    const events = await all(
      body('eve', 'nt: tok', 'en\nda', 'ta: {"text":"A"}\n', '\nevent: done\ndata: {}\n\n'),
    );

    expect(events).toEqual([
      { event: 'token', data: '{"text":"A"}' },
      { event: 'done', data: '{}' },
    ]);
  });

  it('keeps a character whose bytes arrive in different reads', async () => {
    const bytes = new TextEncoder().encode('data: Čaj ☕\n\n');
    const split = bytes.indexOf(0x8c) + 1; // inside "Č" … and "☕" is split by the next cut
    const events = await all(
      body(bytes.slice(0, split - 1), bytes.slice(split - 1, bytes.length - 4), bytes.slice(-4)),
    );

    expect(events).toEqual([{ event: 'message', data: 'Čaj ☕' }]);
  });

  it('understands Windows (CRLF) and old Mac (CR) line endings', async () => {
    const events = await all(body('event: a\r\ndata: 1\r\n\r', '\nevent: b\rdata: 2\r\r'));

    expect(events).toEqual([
      { event: 'a', data: '1' },
      { event: 'b', data: '2' },
    ]);
  });

  it('joins several data lines with new lines and keeps spaces after the first', async () => {
    const events = await all(body('data: line one\ndata:  indented\ndata:\ndata:no space\n\n'));

    expect(events).toEqual([{ event: 'message', data: 'line one\n indented\n\nno space' }]);
  });

  it('skips comments, unknown fields and events without data', async () => {
    const events = await all(
      body(': keep-alive\n\nid: 7\nretry: 1000\nevent: start\n\nevent: token\n: note\ndata: x\n\n'),
    );

    expect(events).toEqual([{ event: 'token', data: 'x' }]);
  });

  it('still reads a last event that has no closing blank line', async () => {
    const events = await all(body('event: done\ndata: {"ok":true}'));

    expect(events).toEqual([{ event: 'done', data: '{"ok":true}' }]);
  });

  it('returns nothing for an empty body', async () => {
    expect(await all(body())).toEqual([]);
  });

  it('gives each event as soon as it is complete, before the rest arrives', async () => {
    let push!: (text: string) => void;
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        push = (text) => controller.enqueue(new TextEncoder().encode(text));
      },
    });
    const events = readServerSentEvents(stream);

    push('data: first\n\ndata: sec');
    expect((await events.next()).value).toEqual({ event: 'message', data: 'first' });

    const second = events.next();
    push('ond\n\n');
    expect((await second).value).toEqual({ event: 'message', data: 'second' });
    await events.return(undefined);
  });

  it('passes on a failed read', async () => {
    const stream = new ReadableStream<Uint8Array>({
      pull(controller) {
        controller.error(new TypeError('network error'));
      },
    });

    await expect(all(stream)).rejects.toThrow('network error');
  });
});
