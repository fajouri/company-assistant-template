import test from 'node:test';
import assert from 'node:assert/strict';
import {runChat} from '../src/chatStream.ts';

async function consume(lines, events = []) {
  const original = globalThis.fetch;
  const encoder = new TextEncoder();
  globalThis.fetch = async () => new Response(new ReadableStream({
    start(controller) {
      for (const line of lines) controller.enqueue(encoder.encode(line));
      controller.close();
    }
  }));
  try {
    await runChat('token', 'question', null, event => events.push(event), new AbortController().signal);
    return events;
  } finally {
    globalThis.fetch = original;
  }
}

test('publishes a complete stream after its terminal event', async () => {
  const events = await consume([
    '{"type":"delta","text":"Answer [1]"}\n',
    '{"type":"done","conversationId":"abc","limited":false,"citations":[]}\n'
  ]);
  assert.equal(events.length, 2);
  assert.equal(events[1].type, 'done');
});

test('rejects EOF without a terminal event without publishing partial answer', async () => {
  const events = [];
  await assert.rejects(consume(['{"type":"delta","text":"partial"}\n'], events), /Incomplete chat stream/);
  assert.deepEqual(events, []);
});

test('rejects a provider error without publishing partial answer', async () => {
  const events = [];
  await assert.rejects(consume([
    '{"type":"delta","text":"partial"}\n',
    '{"type":"error","message":"unavailable"}\n'
  ], events), /Chat stream failed/);
  assert.deepEqual(events, []);
});
