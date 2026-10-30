export type Citation = {index: number; chunkId: number; documentId: string; title: string; version: string};
export type ChatEvent =
  | {type: 'sources'; citations: Citation[]}
  | {type: 'delta'; text: string; provisional: true}
  | {type: 'done'; conversationId: string; limited: boolean; citations: Citation[]}
  | {type: 'error'; code?: 'timeout' | 'provider'; message: string};

export class ChatStreamError extends Error {
  readonly code: 'timeout' | 'provider';
  constructor(code: 'timeout' | 'provider') {
    super('Chat stream failed.');
    this.code = code;
  }
}

function validCitations(value: unknown): value is Citation[] {
  return Array.isArray(value) && value.every(citation => citation &&
    Number.isSafeInteger(citation.index) && citation.index > 0 &&
    Number.isSafeInteger(citation.chunkId) && citation.chunkId > 0 &&
    typeof citation.documentId === 'string' && typeof citation.title === 'string' &&
    typeof citation.version === 'string');
}

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
  let hasText = false;
  let terminal: Extract<ChatEvent, {type: 'done'}> | null = null;
  try {
    for (;;) {
      signal.throwIfAborted();
      const {done, value} = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, {stream: true});
      let index;
      while ((index = buffer.indexOf('\n')) >= 0) {
        const line = buffer.slice(0, index).trim();
        buffer = buffer.slice(index + 1);
        if (line) {
          const event = JSON.parse(line) as ChatEvent;
          if (!event || typeof event !== 'object') throw new Error('Invalid chat event.');
          if (terminal) throw new Error('Event after terminal event.');
          if (event.type === 'delta') {
            if (typeof event.text !== 'string' || event.provisional !== true)
              throw new Error('Invalid provisional delta.');
            hasText ||= event.text.length > 0;
          }
          if (event.type === 'sources' && !validCitations(event.citations))
            throw new Error('Invalid sources event.');
          if (event.type === 'done' &&
              (typeof event.conversationId !== 'string' || !event.conversationId ||
               typeof event.limited !== 'boolean' || !validCitations(event.citations) ||
               (!event.limited && (!hasText || event.citations.length === 0)) ||
               (event.limited && event.citations.length !== 0)))
            throw new Error('Invalid terminal event.');
          if (event.type !== 'sources' && event.type !== 'delta' &&
              event.type !== 'done' && event.type !== 'error')
            throw new Error('Unknown chat event.');
          if (event.type === 'error') throw new ChatStreamError(event.code === 'timeout' ? 'timeout' : 'provider');
          if (event.type === 'done') terminal = event;
          else onEvent(event);
        }
      }
    }
    signal.throwIfAborted();
    buffer += decoder.decode();
    if (!terminal || buffer.trim()) throw new Error('Incomplete chat stream.');
    onEvent(terminal);
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
}
