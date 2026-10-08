/** One server-sent event: its type ("message" when the server gave none) and its data lines joined. */
export interface ServerSentEvent {
  event: string;
  data: string;
}

/**
 * Reads a text/event-stream body as events, as they arrive. Events are separated by a blank line; each has
 * "event: …" and one or more "data: …" lines (joined with "\n"); lines starting with ":" are comments. The bytes
 * may arrive split anywhere — inside a line or inside a multi-byte character — so text is decoded as a stream
 * and only complete events are returned.
 */
export async function* readServerSentEvents(
  body: ReadableStream<Uint8Array>,
): AsyncGenerator<ServerSentEvent> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  try {
    while (true) {
      const { done, value } = await reader.read();
      buffer += done ? decoder.decode() : decoder.decode(value, { stream: true });
      buffer = buffer.replace(/\r\n?/g, '\n');

      let end: number;
      while ((end = buffer.indexOf('\n\n')) >= 0) {
        const event = parseEvent(buffer.slice(0, end));
        buffer = buffer.slice(end + 2);
        if (event) {
          yield event;
        }
      }

      if (done) {
        // A last event without the closing blank line still counts.
        const last = parseEvent(buffer);
        if (last) {
          yield last;
        }
        return;
      }
    }
  } finally {
    reader.releaseLock();
  }
}

function parseEvent(block: string): ServerSentEvent | null {
  let event = 'message';
  const data: string[] = [];
  for (const line of block.split('\n')) {
    if (line === '' || line.startsWith(':')) {
      continue;
    }
    const colon = line.indexOf(':');
    const field = colon < 0 ? line : line.slice(0, colon);
    // One space after the colon is part of the format, not of the value.
    const value = colon < 0 ? '' : line.slice(colon + 1).replace(/^ /, '');
    if (field === 'event') {
      event = value;
    } else if (field === 'data') {
      data.push(value);
    }
  }

  return data.length > 0 ? { event, data: data.join('\n') } : null;
}
