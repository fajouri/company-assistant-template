export type Citation = {index: number; chunkId: number; documentId: string; title: string; version: string};
export type ChatEvent =
  | {type: 'sources'; citations: Citation[]}
  | {type: 'delta'; text: string}
  | {type: 'done'; conversationId: string; limited: boolean; citations: Citation[]}
  | {type: 'error'; code?: 'timeout' | 'provider'; message: string};

export async function runChat(
  csrfToken: string,
  message: string,
  conversationId: string | null,
  onEvent: (event: ChatEvent) => void,
  signal: AbortSignal
) {
  const response = await fetch('/api/chat', {
    method: 'POST',
    headers: {'Content-Type': 'application/json', 'X-CSRF-TOKEN': csrfToken},
    body: JSON.stringify({message, conversationId}),
    signal
  });
  if (!response.ok) {
    let errorText = '';
    try {
      const body = await response.json();
      errorText = body.error ?? '';
    } catch {
      errorText = await response.text();
    }
    throw new Error(errorText || `HTTP ${response.status}`);
  }
  if (!response.body) throw new Error('No response body.');

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  let terminal = false;
  let failed = false;
  const events: ChatEvent[] = [];
  for (;;) {
    const {done, value} = await reader.read();
    if (done) break;
    buffer += decoder.decode(value, {stream: true});
    let index;
    while ((index = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, index).trim();
      buffer = buffer.slice(index + 1);
      if (line) {
        const event = JSON.parse(line) as ChatEvent;
        if (terminal) throw new Error('Event after terminal event.');
        if (event.type === 'done' &&
            (typeof event.conversationId !== 'string' || !event.conversationId ||
             typeof event.limited !== 'boolean' || !Array.isArray(event.citations)))
          throw new Error('Invalid terminal event.');
        if (event.type !== 'sources' && event.type !== 'delta' &&
            event.type !== 'done' && event.type !== 'error')
          throw new Error('Unknown chat event.');
        if (event.type === 'done' || event.type === 'error') {
          terminal = true;
          failed = event.type === 'error';
        }
        events.push(event);
      }
    }
  }
  if (!terminal || buffer.trim()) throw new Error('Incomplete chat stream.');
  if (failed) throw new Error('Chat stream failed.');
  events.forEach(onEvent);
}
