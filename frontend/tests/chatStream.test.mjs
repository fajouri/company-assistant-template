import test from 'node:test';
import assert from 'node:assert/strict';
import {runChat, ChatStreamError} from '../src/chatStream.ts';
import {answerReducer, initialAnswer} from '../src/chatState.ts';

const citation = {index: 1, chunkId: 1, documentId: 'leave', title: 'Leave', version: '1'};
const delta = text => ({type: 'delta', text, provisional: true});
const done = {type: 'done', conversationId: 'abc', limited: false, citations: [citation]};
const encode = event => new TextEncoder().encode(JSON.stringify(event) + '\n');

async function consume(events, onEvent = () => {}) {
  const original = globalThis.fetch;
  globalThis.fetch = async () => new Response(new ReadableStream({
    start(controller) {
      for (const event of events) controller.enqueue(encode(event));
      controller.close();
    }
  }));
  try {
    await runChat('token', 'question', null, onEvent, new AbortController().signal);
  } finally {
    globalThis.fetch = original;
  }
}

test('first delta reaches answer state before provider finishes; split reference confirms at EOF', async () => {
  const original = globalThis.fetch;
  let controller;
  let state = answerReducer(initialAnswer, {type: 'begin'});
  let first;
  const firstDelta = new Promise(resolve => { first = resolve; });
  let settled = false;
  globalThis.fetch = async (url, options) => {
    assert.equal(options.headers['X-CSRF-TOKEN'], 'token');
    assert.deepEqual(JSON.parse(options.body), {message: 'question', conversationId: null});
    return new Response(new ReadableStream({start(value) { controller = value; }}));
  };
  try {
    const running = runChat('token', 'question', null, event => {
      state = answerReducer(state, event);
      if (event.type === 'delta') first();
    }, new AbortController().signal).then(() => { settled = true; });
    controller.enqueue(encode(delta('Answer [')));
    await firstDelta;
    assert.equal(settled, false);
    assert.equal(state.text, 'Answer [');
    assert.equal(state.phase, 'generating');
    assert.deepEqual(state.citations, []);
    controller.enqueue(encode(delta('1]')));
    controller.enqueue(encode({type: 'sources', citations: [citation]}));
    controller.enqueue(encode(done));
    controller.close();
    await running;
    assert.equal(state.phase, 'confirmed');
    assert.equal(state.text, 'Answer [1]');
    assert.deepEqual(state.citations, [citation]);
  } finally { globalThis.fetch = original; }
});

for (const [name, tail, phase] of [
  ['EOF without terminal event', [], 'failed'],
  ['provider failure after delta', [{type: 'error', code: 'provider', message: 'unavailable'}], 'failed'],
  ['timeout after delta', [{type: 'error', code: 'timeout', message: 'unavailable'}], 'timeout'],
  ['success without citations', [{...done, citations: []}], 'failed'],
  ['malformed citation', [{...done, citations: [{index: 1}]}], 'failed'],
  ['unmarked delta', [{type: 'delta', text: 'unmarked'}], 'failed'],
  ['duplicate terminal event', [done, done], 'failed'],
  ['delta after terminal event', [done, delta('late')], 'failed']
]) {
  test(`${name} withdraws provisional text and never confirms it`, async () => {
    let state = answerReducer(initialAnswer, {type: 'begin'});
    const published = [];
    await assert.rejects(async () => {
      try {
        await consume([delta('Partial [1]'), ...tail], event => {
          published.push(event);
          state = answerReducer(state, event);
        });
      } catch (error) {
        state = answerReducer(state, {type: error instanceof ChatStreamError && error.code === 'timeout' ? 'timeout' : 'failed'});
        throw error;
      }
    });
    assert.equal(published[0].type, 'delta');
    assert.equal(published.some(event => event.type === 'done'), false);
    assert.equal(state.text, '');
    assert.deepEqual(state.citations, []);
    assert.equal(state.phase, phase);
  });
}

test('limited terminal result withdraws invalid or unreferenced provisional answer', async () => {
  let state = answerReducer(initialAnswer, {type: 'begin'});
  await consume([delta('Unknown reference [99]'), {...done, limited: true, citations: []}],
    event => { state = answerReducer(state, event); });
  assert.equal(state.phase, 'limited');
  assert.equal(state.text, '');
  assert.deepEqual(state.citations, []);
});

test('user cancellation after delta withdraws answer and stays distinct from timeout', async () => {
  const original = globalThis.fetch;
  const abort = new AbortController();
  let state = answerReducer(initialAnswer, {type: 'begin'});
  globalThis.fetch = async () => new Response(new ReadableStream({
    start(controller) {
      controller.enqueue(encode(delta('Partial')));
      abort.signal.addEventListener('abort', () => controller.error(abort.signal.reason));
    }
  }));
  try {
    await assert.rejects(async () => {
      try {
        await runChat('token', 'question', null, event => {
          state = answerReducer(state, event);
          abort.abort();
        }, abort.signal);
      } catch (error) {
        state = answerReducer(state, {type: abort.signal.aborted ? 'cancelled' : 'failed'});
        throw error;
      }
    }, {name: 'AbortError'});
    assert.equal(state.phase, 'cancelled');
    assert.equal(state.text, '');
    assert.deepEqual(state.citations, []);
  } finally { globalThis.fetch = original; }
});

test('fragmented NDJSON is parsed incrementally', async () => {
  const original = globalThis.fetch;
  const bytes = encode(delta('Answer [1]'));
  globalThis.fetch = async () => new Response(new ReadableStream({
    start(controller) {
      for (const byte of bytes) controller.enqueue(Uint8Array.of(byte));
      controller.enqueue(encode(done));
      controller.close();
    }
  }));
  try {
    const events = [];
    await runChat('token', 'question', null, event => events.push(event), new AbortController().signal);
    assert.deepEqual(events, [delta('Answer [1]'), done]);
  } finally { globalThis.fetch = original; }
});
